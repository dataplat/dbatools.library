#if NET8_0_OR_GREATER
using Microsoft.Data.SqlClient;
using System;
using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;

namespace Dataplat.Dbatools.Connection
{
    /// <summary>
    /// Generates SQL Server integrated-authentication tokens from an explicit Windows credential.
    /// </summary>
    public sealed class NetworkCredentialSspiContextProvider : SspiContextProvider, IDisposable
    {
        private readonly NetworkCredential credential;
        private readonly byte[] credentialIdentity;
        private readonly string principal;
        private NegotiateAuthentication authentication;
        private string resource;
        private bool disposed;

        /// <summary>
        /// The case-insensitive Windows principal ("domain\user" or "user") this provider
        /// authenticates as. Password-free, safe to use as part of a registry/cache key.
        /// </summary>
        public string Principal
        {
            get { return principal; }
        }

        /// <summary>
        /// Creates a provider that authenticates with the supplied Windows credential.
        /// </summary>
        /// <param name="credential">The Windows credential used for Negotiate authentication.</param>
        public NetworkCredentialSspiContextProvider(NetworkCredential credential)
        {
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            this.credential = new NetworkCredential(credential.UserName, credential.Password, credential.Domain);
            principal = String.IsNullOrEmpty(credential.Domain)
                ? credential.UserName
                : credential.Domain + "\\" + credential.UserName;
            using (SHA256 sha256 = SHA256.Create())
            {
                credentialIdentity = sha256.ComputeHash(Encoding.UTF8.GetBytes(
                    principal.ToUpperInvariant() + "\0" + credential.Password));
            }
        }

        /// <summary>
        /// Compares providers by Windows principal and credential identity for the SqlClient pool key.
        /// </summary>
        public override bool Equals(object obj)
        {
            NetworkCredentialSspiContextProvider other = obj as NetworkCredentialSspiContextProvider;
            return other != null && String.Equals(principal, other.principal, StringComparison.OrdinalIgnoreCase) &&
                CryptographicOperations.FixedTimeEquals(credentialIdentity, other.credentialIdentity);
        }

        /// <summary>
        /// Returns the case-insensitive Windows principal hash used by the SqlClient pool key.
        /// Credential identity is intentionally excluded to avoid disclosing a password-derived hash value.
        /// </summary>
        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(principal);
        }

        /// <inheritdoc />
        protected override bool GenerateContext(
            ReadOnlySpan<byte> incomingBlob,
            IBufferWriter<byte> outgoingBlobWriter,
            SspiAuthenticationParameters authParams)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NetworkCredentialSspiContextProvider));
            if (outgoingBlobWriter == null)
                throw new ArgumentNullException(nameof(outgoingBlobWriter));
            if (authParams == null)
                throw new ArgumentNullException(nameof(authParams));

            if (authentication == null || authentication.IsAuthenticated ||
                !String.Equals(resource, authParams.Resource, StringComparison.Ordinal))
            {
                if (authentication != null)
                    authentication.Dispose();

                resource = authParams.Resource;
                authentication = new NegotiateAuthentication(new NegotiateAuthenticationClientOptions
                {
                    Package = "Negotiate",
                    TargetName = resource,
                    Credential = credential
                });
            }

            try
            {
                NegotiateAuthenticationStatusCode statusCode;
                byte[] outgoingBlob = authentication.GetOutgoingBlob(incomingBlob, out statusCode);
                if (statusCode != NegotiateAuthenticationStatusCode.Completed &&
                    statusCode != NegotiateAuthenticationStatusCode.ContinueNeeded)
                {
                    ResetAuthentication();
                    return false;
                }

                if (outgoingBlob != null)
                {
                    Span<byte> destination = outgoingBlobWriter.GetSpan(outgoingBlob.Length);
                    outgoingBlob.AsSpan().CopyTo(destination);
                    outgoingBlobWriter.Advance(outgoingBlob.Length);
                }

                if (statusCode == NegotiateAuthenticationStatusCode.Completed)
                    ResetAuthentication();

                return true;
            }
            catch
            {
                ResetAuthentication();
                throw;
            }
        }

        private void ResetAuthentication()
        {
            if (authentication != null)
                authentication.Dispose();
            authentication = null;
            resource = null;
        }

        /// <summary>
        /// Releases the active Negotiate authentication context.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            ResetAuthentication();
        }
    }
}
#endif
