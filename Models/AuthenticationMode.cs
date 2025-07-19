using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Models
{
    /// <summary>
    /// Represents the authentication modes available for connections.
    /// </summary>
    public enum AuthenticationMode
    {
        /// <summary>
        /// Basic authentication mode.
        /// </summary>
        Basic,
        /// <summary>
        /// Digest authentication mode.
        /// </summary>
        Digest,
        /// <summary>
        /// NTLM authentication mode.
        /// </summary>
        NTLM,
        /// <summary>
        /// OAuth authentication mode.
        /// </summary>
        OAuth,
        WSUsernameToken
    }
}
