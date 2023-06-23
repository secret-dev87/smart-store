﻿using Microsoft.AspNetCore.Http;
using Smartstore.Core.Configuration;

namespace Smartstore.Core.Identity
{
    public class PrivacySettings : ISettings
    {
        /// <summary>
        /// Gets or sets the CookieManager display type.
        /// </summary>
        public CookieManagerDisplayType CookieManagerDisplayType { get; set; } = CookieManagerDisplayType.CountryConfigured;

        /// <summary>
        /// Gets or sets a value indicating whether the cookie dialog will be display in a modal dialog.
        /// </summary>
        public bool ModalCookieConsent { get; set; } = true;

        /// <summary>
        /// Gets or sets the global SameSiteMode for cookies.
        /// </summary>
        public SameSiteMode SameSiteMode { get; set; } = SameSiteMode.Lax;

        /// <summary>
        /// Gets or sets a value indicating whether to store last IP address for each customer.
        /// </summary>
        public bool StoreLastIpAddress { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to display a checkbox to the customer where he can agree to privacy terms.
        /// </summary>
        public bool DisplayGdprConsentOnForms { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the full name field is required on contact-us requests.
        /// </summary>
        public bool FullNameOnContactUsRequired { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the full name field is required on product requests.
        /// </summary>
        public bool FullNameOnProductRequestRequired { get; set; }

        /// <summary>
        /// Gets or sets cookie infos (JSON serialized).
        /// </summary>
        public string CookieInfos { get; set; } = string.Empty;
    }
}
