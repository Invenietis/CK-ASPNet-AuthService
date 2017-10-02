using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IdentityServer4.Validation;
using IdentityServer4.Models;
using IdentityServer4;
using IdentityServer4.Test;
using Microsoft.AspNetCore.Authentication.Google;

namespace IdServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication().AddGoogle( o =>
            {
                o.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;
                o.ClientId = "708996912208-9m4dkjb5hscn7cjrn5u0r4tbgkbj1fko.apps.googleusercontent.com";
                o.ClientSecret = "wdfPY6t8H8cecgjlxud__4Gh";
            } );
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryIdentityResources(new List<IdentityResource>
                {
                    new IdentityResources.OpenId(),
                    new IdentityResources.Profile(),
                })
                .AddInMemoryClients(new Client[]
                {
                     new Client
                    {
                        ClientId = "WebApp",
                        ClientName = "The test WebApp",

                        AllowedGrantTypes = GrantTypes.Implicit,

                        // Where to redirect to after login
                        RedirectUris = { "http://localhost:4324/signin-oidc" },

                        // Where to redirect to after logout
                        PostLogoutRedirectUris = { "http://localhost:4324/signout-callback-oidc" },

                        // Scopes that client has access to
                        AllowedScopes = new List<string>
                        {
                            IdentityServerConstants.StandardScopes.OpenId,
                            IdentityServerConstants.StandardScopes.Profile,
                        },

                        // Secret for client authentication
                        ClientSecrets =
                        {
                            new Secret("WebApp.Secret")
                        },

                    }
               })
                .AddInMemoryApiResources( Enumerable.Empty<ApiResource>() )
                // The AddTestUsers extension method does a couple of things under the hood
                //  - adds support for the resource owner password grant
                //  - adds support to user related services typically used by a login UI(we’ll use that in the next quickstart)
                //  - adds support for a profile service based on the test users(you’ll learn more about that in the next quickstart)
                .AddTestUsers(new List<TestUser>
                {
                    new TestUser
                    {
                        SubjectId = "Alice_has_only_basic_authentication",
                        Username = "alice",
                        Password = "password"
                    },
                    new TestUser
                    {
                        SubjectId = "Bob_is_totally_unknown",
                        Username = "bob",
                        Password = "password"
                    },
                    new TestUser
                    {
                        SubjectId = "Carol_is_Basic_and_Oidc_registered",
                        Username = "carol",
                        Password = "password"
                    }
                } );
            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole();

            app.UseDeveloperExceptionPage();

            app.UseIdentityServer();


            app.UseStaticFiles();
            app.UseMvcWithDefaultRoute();
        }
    }
}
