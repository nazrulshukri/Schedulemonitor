//------------------------------------------------------------------------------
// <copyright file="UserAuthenticator.cs" company="Nexperia">
// Copyright (C) Nexperia. All rights reserved.
// </copyright>
// <project>Awacs MES interface.</project>
// <summary>Credential check behind the ATCB assembly recipe sign-in page.</summary>
//------------------------------------------------------------------------------
using System;
using System.Configuration;
using System.DirectoryServices.AccountManagement;

namespace EWFM.AwacsMes.Security
{
    /// <summary>
    /// Validates the credentials typed on Login.aspx.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This checks the user against the Windows domain named by the
    /// <c>loginDomain</c> app setting, so no user table and no password column
    /// has to be kept anywhere in this application. Membership of a single group
    /// can be required on top of that with <c>loginGroup</c>.
    /// </para>
    /// <para>
    /// The whole credential check is this one method on purpose. If the ATCB
    /// recipe users are kept in the awacs database rather than in the domain,
    /// replace the body of <see cref="Validate"/> with that query - the page does
    /// not care where the answer comes from, and nothing else in the project calls
    /// into here.
    /// </para>
    /// </remarks>
    public static class UserAuthenticator
    {
        /// <summary>
        /// Answers whether the specified credentials are valid.
        /// </summary>
        /// <param name="userId">The user ID as typed on the sign-in page.</param>
        /// <param name="password">The password as typed on the sign-in page.</param>
        /// <returns>True when the credentials are accepted.</returns>
        /// <exception cref="ConfigurationErrorsException">
        /// The <c>loginDomain</c> app setting is missing or empty.
        /// </exception>
        /// <exception cref="PrincipalException">The domain could not be reached.</exception>
        public static bool Validate(string userId, string password)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
            {
                // An empty password is not a failed sign in to the domain: an
                // unbound LDAP connect can come back as success. Refused here so it
                // never reaches ValidateCredentials.
                return false;
            }

            string domain = ConfigurationManager.AppSettings["loginDomain"];

            if (string.IsNullOrEmpty(domain))
            {
                // Config left unfinished. Thrown rather than returned as "wrong
                // password", so the page reports an outage and the event says why.
                throw new ConfigurationErrorsException(
                    "The loginDomain app setting is not configured. Sign in cannot validate credentials.");
            }

            // A user ID typed as DOMAIN\user or user@domain is handed to the
            // directory as it stands; ValidateCredentials accepts both forms.
            using (PrincipalContext context = new PrincipalContext(ContextType.Domain, domain))
            {
                if (!context.ValidateCredentials(userId, password, ContextOptions.Negotiate))
                {
                    return false;
                }

                string requiredGroup = ConfigurationManager.AppSettings["loginGroup"];

                if (string.IsNullOrEmpty(requiredGroup))
                {
                    // No group configured: a valid domain account is enough.
                    return true;
                }

                return IsMemberOf(context, userId, requiredGroup);
            }
        }

        /// <summary>
        /// Answers whether the specified user belongs to the specified group.
        /// </summary>
        /// <param name="context">The domain context to search.</param>
        /// <param name="userId">The user ID whose membership is checked.</param>
        /// <param name="groupName">The name of the required group.</param>
        /// <returns>True when the user is a member of the group.</returns>
        private static bool IsMemberOf(PrincipalContext context, string userId, string groupName)
        {
            // The credentials are already proven at this point, so the lookup runs
            // as the application pool identity and needs read access to the
            // directory. Strip any DOMAIN\ prefix first - FindByIdentity matches the
            // account name, not the qualified form.
            int separator = userId.IndexOf('\\');
            string accountName = separator >= 0 ? userId.Substring(separator + 1) : userId;

            using (UserPrincipal user = UserPrincipal.FindByIdentity(context, accountName))
            {
                if (user == null)
                {
                    return false;
                }

                using (GroupPrincipal group = GroupPrincipal.FindByIdentity(context, groupName))
                {
                    return group != null && user.IsMemberOf(group);
                }
            }
        }
    }
}
