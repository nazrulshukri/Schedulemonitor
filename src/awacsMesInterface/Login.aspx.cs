//------------------------------------------------------------------------------
// <copyright file="Login.aspx.cs" company="Nexperia">
// Copyright (C) Nexperia. All rights reserved.
// </copyright>
// <project>Awacs MES interface.</project>
// <summary>Sign-in page for the ATCB assembly recipe pages.</summary>
//------------------------------------------------------------------------------
using System;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using EWFM.AwacsMes.Diagnostics;
using EWFM.AwacsMes.Security;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Sign-in page. Validates the credentials through <see cref="UserAuthenticator"/>
    /// and, on success, issues the forms authentication ticket the recipe pages
    /// check.
    /// </summary>
    /// <remarks>
    /// Service.asmx is deliberately left outside this: the workstations call it
    /// machine to machine with no user and no cookie jar, so Web.config keeps an
    /// anonymous &lt;location&gt; entry for it. Protect pages, never the service.
    /// </remarks>
    public partial class Login : Page
    {
        /// <summary>
        /// The message shown for every rejected sign-in. One text for a bad user ID
        /// and for a bad password alike, so the page cannot be used to find out
        /// which user IDs exist.
        /// </summary>
        private const string SignInFailedMessage = "Sign in failed. Check your user ID and password.";

        /// <summary>
        /// Handles the page load.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!this.Request.IsAuthenticated)
            {
                // Signed out: show the sign-in form, which is what the markup
                // starts as.
                return;
            }

            string returnUrl = this.GetSafeReturnUrl();

            if (!this.PointsAtThisPage(returnUrl))
            {
                // Signed in already and there is somewhere to be: go there rather
                // than asking for a password that has already been given.
                this.Response.Redirect(returnUrl, false);
                this.Context.ApplicationInstance.CompleteRequest();
                return;
            }

            // Signed in with nowhere to go, because the forms defaultUrl still
            // points at this page - the recipe pages are not built yet. Redirecting
            // here would be a loop, so show who is signed in instead. Point
            // defaultUrl at the landing page once there is one and this branch stops
            // being reached.
            this.ShowSignedIn();
        }

        /// <summary>
        /// Handles the sign-in button.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        protected void btnSignIn_Click(object sender, EventArgs e)
        {
            string userId = (this.txtUserId.Text ?? string.Empty).Trim();
            string password = this.txtPassword.Text ?? string.Empty;

            // Never leave the password in the rendered HTML, on any path out of
            // this method.
            this.txtPassword.Text = string.Empty;

            if (userId.Length == 0 || password.Length == 0)
            {
                this.ShowError("Enter your user ID and password.");
                return;
            }

            TraceLog traceLog = TraceLog.Create("Login");

            try
            {
                if (!UserAuthenticator.Validate(userId, password))
                {
                    traceLog.WriteEntry(string.Format("Sign in rejected for user ID {0}.", userId));
                    this.ShowError(SignInFailedMessage);
                    return;
                }

                traceLog.WriteEntry(string.Format("Sign in accepted for user ID {0}.", userId));

                // false: a session cookie, not a persistent one. A recipe change
                // signed off by whoever used the browser last is worse than signing
                // in again.
                FormsAuthentication.SetAuthCookie(userId, false);

                // Not RedirectFromLoginPage: that trusts the ReturnUrl in the query
                // string, and GetSafeReturnUrl is where this page decides which
                // URLs it is willing to send a signed-in user to.
                this.Response.Redirect(this.GetSafeReturnUrl(), false);
                this.Context.ApplicationInstance.CompleteRequest();
            }
            catch (Exception ex)
            {
                // A directory that cannot be reached is an outage, not a wrong
                // password, and the two have to read differently or the shift spends
                // the outage retyping passwords.
                traceLog.LogException(ex);
                this.ShowError("Sign in is unavailable right now. Contact IT support if this continues.");
            }
            finally
            {
                traceLog.Close();
            }
        }

        /// <summary>
        /// Handles the sign-out button.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        protected void btnSignOut_Click(object sender, EventArgs e)
        {
            FormsAuthentication.SignOut();
            FormsAuthentication.RedirectToLoginPage();
        }

        /// <summary>
        /// Replaces the sign-in form with the name of the signed-in user.
        /// </summary>
        private void ShowSignedIn()
        {
            this.litUserName.Text = HttpUtility.HtmlEncode(this.User.Identity.Name);
            this.phSignIn.Visible = false;
            this.phSignedIn.Visible = true;
        }

        /// <summary>
        /// Shows an error message on the page.
        /// </summary>
        /// <param name="message">The message to show.</param>
        private void ShowError(string message)
        {
            this.lblError.Text = HttpUtility.HtmlEncode(message);
            this.lblError.Visible = true;
        }

        /// <summary>
        /// Answers the ReturnUrl of the request when it is a local path, and the
        /// configured default URL otherwise.
        /// </summary>
        /// <returns>A URL that is safe to redirect to.</returns>
        /// <remarks>
        /// ReturnUrl arrives in the query string, so it is whatever the caller put
        /// there. Redirecting to it unchecked turns this page into an open redirect:
        /// a link to Login.aspx?ReturnUrl=http://elsewhere/ would bounce the user
        /// off site straight after a successful sign in.
        /// </remarks>
        private string GetSafeReturnUrl()
        {
            string returnUrl = this.Request.QueryString["ReturnUrl"];

            if (!string.IsNullOrEmpty(returnUrl) && IsLocalUrl(returnUrl))
            {
                return returnUrl;
            }

            return FormsAuthentication.DefaultUrl;
        }

        /// <summary>
        /// Answers whether the specified URL points back at this application.
        /// </summary>
        /// <param name="url">The URL to check.</param>
        /// <returns>True when the URL is a local path.</returns>
        private static bool IsLocalUrl(string url)
        {
            // "//host" and "/\host" are protocol relative and leave the site, so a
            // leading slash on its own is not enough.
            return url.StartsWith("/", StringComparison.Ordinal)
                && !url.StartsWith("//", StringComparison.Ordinal)
                && !url.StartsWith("/\\", StringComparison.Ordinal);
        }

        /// <summary>
        /// Answers whether the specified URL is this page.
        /// </summary>
        /// <param name="url">The URL to compare against the current request.</param>
        /// <returns>True when the URL names this page.</returns>
        private bool PointsAtThisPage(string url)
        {
            return string.Equals(
                GetFileName(url),
                GetFileName(this.Request.Path),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Answers the file name part of the specified URL.
        /// </summary>
        /// <param name="url">The URL to take the file name from.</param>
        /// <returns>The file name, which is empty for a URL that ends in a slash.</returns>
        private static string GetFileName(string url)
        {
            // Hand rolled rather than VirtualPathUtility: DefaultUrl may arrive as a
            // relative "Login.aspx" or as an application absolute path depending on
            // how it was configured, and VirtualPathUtility throws on the first.
            int marker = url.IndexOfAny(new[] { '?', '#' });
            string path = marker >= 0 ? url.Substring(0, marker) : url;
            int slash = path.LastIndexOf('/');

            return slash >= 0 ? path.Substring(slash + 1) : path;
        }
    }
}
