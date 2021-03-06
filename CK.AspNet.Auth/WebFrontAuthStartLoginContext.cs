using CK.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet.Auth
{
    /// <summary>
    /// Captures initial login request information and provides a context to interact with the flow
    /// before challenging the actual remote authentication.
    /// </summary>
    public sealed class WebFrontAuthStartLoginContext
    {
        readonly WebFrontAuthService _webFrontAuthService;

        string? _errorId;
        string? _errorText;

        internal WebFrontAuthStartLoginContext(
            HttpContext ctx,
            WebFrontAuthService authService,
            string scheme,
            IAuthenticationInfo current,
            IEnumerable<KeyValuePair<string, StringValues>> userData,
            string returnUrl,
            string callerOrigin
            )
        {
            Debug.Assert( ctx != null && authService != null );
            Debug.Assert( scheme != null );
            Debug.Assert( current != null );
            Debug.Assert( userData != null );
            HttpContext = ctx;
            _webFrontAuthService = authService;
            Scheme = scheme;
            Current = current;
            UserData = new Dictionary<string, StringValues>();
            foreach( var d in userData ) UserData.Add( d.Key, d.Value );
            ReturnUrl = returnUrl;
            CallerOrigin = callerOrigin;
        }

        /// <summary>
        /// Gets the http context.
        /// </summary>
        public HttpContext HttpContext { get; }

        /// <summary>
        /// Gets the current authentication.
        /// </summary>
        public IAuthenticationInfo Current { get; }

        /// <summary>
        /// Gets or sets the scheme to challenge.
        /// Never null or empty.
        /// </summary>
        public string Scheme { get; set; }

        /// <summary>
        /// Gets or sets the return url.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        /// Gets or sets the optional caller origin.
        /// </summary>
        public string CallerOrigin { get; set; }

        /// <summary>
        /// Gets the mutable user data.
        /// </summary>
        public IDictionary<string, StringValues> UserData { get; }

        /// <summary>
        /// Gets whether an error has been set.
        /// </summary>
        public bool HasError => _errorId != null;

        /// <summary>
        /// Sets an error message.
        /// The returned error contains the <paramref name="errorId"/> and <paramref name="errorMessage"/>, the <see cref="Scheme"/>
        /// and <see cref="UserData"/>.
        /// Can be called multiple times: new error information replaces the previous one.
        /// </summary>
        /// <param name="errorId">Error identifier (a dotted identifier string).</param>
        /// <param name="errorMessage">The error message in clear text.</param>
        public void SetError( string errorId, string errorMessage )
        {
            if( string.IsNullOrWhiteSpace( errorId ) ) throw new ArgumentNullException( nameof( errorId ) );
            if( string.IsNullOrWhiteSpace( errorMessage ) ) throw new ArgumentNullException( nameof( errorMessage ) );
            _errorId = errorId;
            _errorText = errorMessage;
        }

        /// <summary>
        /// Captures dynamic scopes from optional IWebFrontAuthDynamicScopeProvider.GetScopesAsync call.
        /// This is internal since it is the optional <see cref="IWebFrontAuthDynamicScopeProvider"/> that is used
        /// to set it from <see cref="WebFrontAuthService.OnHandlerStartLogin(Core.IActivityMonitor, WebFrontAuthStartLoginContext)"/>.
        /// </summary>
        internal string[]? DynamicScopes;

        internal Task SendError( string deviceId )
        {
            Debug.Assert( HasError );
            Debug.Assert( _errorId != null && _errorText != null );

            return _webFrontAuthService.SendRemoteAuthenticationError(
                        HttpContext,
                        deviceId,
                        ReturnUrl,
                        CallerOrigin,
                        _errorId,
                        _errorText,
                        Scheme,
                        null,
                        UserData,
                        null );
        }

    }

}
