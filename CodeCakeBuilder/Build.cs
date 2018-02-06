using Cake.Common.Build;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Solution;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.Build;
using Cake.Common.Tools.DotNetCore.Pack;
using Cake.Common.Tools.DotNetCore.Restore;
using Cake.Common.Tools.DotNetCore.Test;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Push;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CK.Text;
using Cake.Common.Tools.NUnit;

namespace CodeCake
{
    /// <summary>
    /// Standard build "script".
    /// </summary>
    [AddPath( "CodeCakeBuilder/Tools" )]
    [AddPath( "packages/**/tools*" )]
    public class Build : CodeCakeHost
    {
        public Build()
        {
            Cake.Log.Verbosity = Verbosity.Diagnostic;

            const string solutionName = "CK-AspNet-Auth";
            const string solutionFileName = solutionName + ".sln";
            var coreBuildFile = Cake.File( "CodeCakeBuilder/CoreBuild.proj" );
            var releasesDir = Cake.Directory( "CodeCakeBuilder/Releases" );

            var projects = Cake.ParseSolution( solutionFileName )
                           .Projects
                           .Where( p => !(p is SolutionFolder)
                                        && p.Name != "CodeCakeBuilder" );

            // We do not publish .Tests projects for this solution.
            var projectsToPublish = projects
                                        .Where( p => !p.Path.Segments.Contains( "Tests" ) );

            SimpleRepositoryInfo gitInfo = Cake.GetSimpleRepositoryInfo();

            // Configuration is either "Debug" or "Release".
            string configuration = "Debug";

            Task( "Check-Repository" )
                .Does( () =>
                 {
                     if( !gitInfo.IsValid )
                     {
                         if( Cake.IsInteractiveMode()
                             && Cake.ReadInteractiveOption( "Repository is not ready to be published. Proceed anyway?", 'Y', 'N' ) == 'Y' )
                         {
                             Cake.Warning( "GitInfo is not valid, but you choose to continue..." );
                         }
                         else if( !Cake.AppVeyor().IsRunningOnAppVeyor ) throw new Exception( "Repository is not ready to be published." );
                     }

                     if( gitInfo.IsValidRelease
                          && (gitInfo.PreReleaseName.Length == 0 || gitInfo.PreReleaseName == "rc") )
                     {
                         configuration = "Release";
                     }

                     Cake.Information( "Publishing {0} projects with version={1} and configuration={2}: {3}",
                         projectsToPublish.Count(),
                         gitInfo.SafeSemVersion,
                         configuration,
                         projectsToPublish.Select( p => p.Name ).Concatenate() );
                 } );

            Task( "Clean" )
                .IsDependentOn( "Check-Repository" )
                .Does( () =>
                 {
                     Cake.CleanDirectories( projects.Select( p => p.Path.GetDirectory().Combine( "bin" ) ) );
                     Cake.CleanDirectories( releasesDir );
                 } );


            Task( "Build" )
                .IsDependentOn( "Clean" )
                .IsDependentOn( "Check-Repository" )
                .Does( () =>
                 {
                     Cake.DotNetCoreBuild( coreBuildFile,
                         new DotNetCoreBuildSettings().AddVersionArguments( gitInfo, s =>
                         {
                             s.Configuration = configuration;
                         } ) );
                 } );

            Task( "Unit-Testing" )
                .IsDependentOn( "Build" )
                .WithCriteria( () => !Cake.IsInteractiveMode()
                                        || Cake.ReadInteractiveOption( "Run unit tests?", 'Y', 'N' ) == 'Y' )
               .Does( () =>
                {
                    var testDlls = projects
                                     .Where( p => p.Name.EndsWith( ".Tests" )
                                                 && !p.Path.Segments.Contains( "Integration" ) )
                                     .Select( p =>
                                     new
                                     {
                                         ProjectPath = p.Path.GetDirectory(),
                                         NetCoreAppDll = p.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/netcoreapp2.0/" + p.Name + ".dll" ),
                                         Net461Dll = p.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/net461/" + p.Name + ".dll" ),
                                     } );
                    foreach( var test in testDlls )
                    {
                        if( System.IO.File.Exists( test.Net461Dll.FullPath ) )
                        {
                            Cake.Information( $"Testing: {test.Net461Dll}" );
                            Cake.NUnit( test.Net461Dll.FullPath, new NUnitSettings() { Framework = "v4.5" } );
                        }
                        if( System.IO.File.Exists( test.NetCoreAppDll.FullPath ) )
                        {
                            Cake.Information( $"Testing: {test.NetCoreAppDll}" );
                            Cake.DotNetCoreExecute( test.NetCoreAppDll );
                        }
                    }
                } );

            Task( "Build-Integration-Projects" )
                .IsDependentOn( "Unit-Testing" )
                .Does( () =>
                {
                    // Use WebApp.Tests to generate the StObj assembly.
                    var webAppTests = projects.Single( p => p.Name == "WebApp.Tests" );
                    var path = webAppTests.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/net461/WebApp.Tests.dll" );
                    Cake.NUnit( path.FullPath, new NUnitSettings() { Include = "GenerateStObjAssembly" } );

                    var webApp = projects.Single( p => p.Name == "WebApp" );
                    Cake.DotNetCoreBuild( webApp.Path.FullPath,
                         new DotNetCoreBuildSettings().AddVersionArguments( gitInfo, s =>
                         {
                             s.Configuration = configuration;
                         } ) );
                } );

            Task( "Integration-Testing" )
                .IsDependentOn( "Build-Integration-Projects" )
                .WithCriteria( () => !Cake.IsInteractiveMode()
                                     || Cake.ReadInteractiveOption( "Run integration tests?", 'Y', 'N' ) == 'Y' )
                .Does( () =>
                {
                    var testDlls = projects
                                        .Where( p => p.Name.EndsWith( ".Tests" )
                                                    && p.Path.Segments.Contains( "Integration" ) )
                                        .Select( p =>
                                        new
                                        {
                                            ProjectPath = p.Path.GetDirectory(),
                                            NetCoreAppDll = p.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/netcoreapp2.0/" + p.Name + ".dll" ),
                                            Net461Dll = p.Path.GetDirectory().CombineWithFilePath( "bin/" + configuration + "/net461/" + p.Name + ".dll" ),
                                        } );
                    try
                    {
                        foreach( var test in testDlls )
                        {
                            if( System.IO.File.Exists( test.Net461Dll.FullPath ) )
                            {
                                Cake.Information( $"Testing: {test.Net461Dll}" );
                                Cake.NUnit( test.Net461Dll.FullPath, new NUnitSettings() { Framework = "v4.5" } );
                            }
                            if( System.IO.File.Exists( test.NetCoreAppDll.FullPath ) )
                            {
                                Cake.Information( $"Testing: {test.NetCoreAppDll}" );
                                Cake.DotNetCoreExecute( test.NetCoreAppDll );
                            }
                        }
                    }
                    finally
                    {
                        if( Cake.AppVeyor().IsRunningOnAppVeyor )
                        {
                            foreach( var fLog in Cake.GetFiles( "Tests/Integration/WebApp/WebAppLogs/Textual/*.*" ) )
                            {
                                Cake.AppVeyor().UploadArtifact( fLog );
                            }
                        }
                    }
                } );


            Task( "Create-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .IsDependentOn( "Unit-Testing" )
                .IsDependentOn( "Integration-Testing" )
                .Does( () =>
                 {
                     Cake.CreateDirectory( releasesDir );
                     foreach( SolutionProject p in projectsToPublish )
                     {
                         Cake.Warning( p.Path.GetDirectory().FullPath );
                         var s = new DotNetCorePackSettings();
                         s.ArgumentCustomization = args => args.Append( "--include-symbols" );
                         s.NoBuild = true;
                         s.Configuration = configuration;
                         s.OutputDirectory = releasesDir;
                         s.AddVersionArguments( gitInfo );
                         Cake.DotNetCorePack( p.Path.GetDirectory().FullPath, s );
                     }
                 } );

            Task( "Push-NuGet-Packages" )
                .WithCriteria( () => gitInfo.IsValid )
                .IsDependentOn( "Create-NuGet-Packages" )
                .Does( () =>
                 {
                     IEnumerable<FilePath> nugetPackages = Cake.GetFiles( releasesDir.Path + "/*.nupkg" );
                     if( Cake.IsInteractiveMode() )
                     {
                         var localFeed = Cake.FindDirectoryAbove( "LocalFeed" );
                         if( localFeed != null )
                         {
                             Cake.Information( "LocalFeed directory found: {0}", localFeed );
                             if( Cake.ReadInteractiveOption( "Do you want to publish to LocalFeed?", 'Y', 'N' ) == 'Y' )
                             {
                                 Cake.CopyFiles( nugetPackages, localFeed );
                             }
                         }
                     }
                     if( gitInfo.IsValidRelease )
                     {
                         if( gitInfo.PreReleaseName == ""
                             || gitInfo.PreReleaseName == "prerelease"
                             || gitInfo.PreReleaseName == "rc" )
                         {
                             PushNuGetPackages( "NUGET_API_KEY", "https://www.nuget.org/api/v2/package", nugetPackages );
                         }
                         else
                         {
                             // An alpha, beta, delta, epsilon, gamma, kappa goes to invenietis-preview.
                             PushNuGetPackages( "MYGET_PREVIEW_API_KEY", "https://www.myget.org/F/invenietis-preview/api/v2/package", nugetPackages );
                         }
                     }
                     else
                     {
                         Debug.Assert( gitInfo.IsValidCIBuild );
                         PushNuGetPackages( "MYGET_CI_API_KEY", "https://www.myget.org/F/invenietis-ci/api/v2/package", nugetPackages );
                     }
                     if( Cake.AppVeyor().IsRunningOnAppVeyor )
                     {
                         Cake.AppVeyor().UpdateBuildVersion( gitInfo.SafeNuGetVersion );
                     }
                 } );

            // The Default task for this script can be set here.
            Task( "Default" )
                .IsDependentOn( "Push-NuGet-Packages" );

        }

        void PushNuGetPackages( string apiKeyName, string pushUrl, IEnumerable<FilePath> nugetPackages )
        {
            // Resolves the API key.
            var apiKey = Cake.InteractiveEnvironmentVariable( apiKeyName );
            if( string.IsNullOrEmpty( apiKey ) )
            {
                Cake.Information( $"Could not resolve {apiKeyName}. Push to {pushUrl} is skipped." );
            }
            else
            {
                var settings = new NuGetPushSettings
                {
                    Source = pushUrl,
                    ApiKey = apiKey,
                    Verbosity = NuGetVerbosity.Detailed
                };

                foreach( var nupkg in nugetPackages.Where( p => !p.FullPath.EndsWith( ".symbols.nupkg" ) ) )
                {
                    Cake.Information( $"Pushing '{nupkg}' to '{pushUrl}'." );
                    Cake.NuGetPush( nupkg, settings );
                }
            }
        }
    }
}
