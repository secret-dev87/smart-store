﻿using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Smartstore.Core.Seo;
using Smartstore.Core.Seo.Routing;
using Smartstore.Core.Localization.Routing;

namespace Smartstore.Core.Localization
{
    /// <summary>
    /// Applies all configured rules for SEO friendly URLs.
    /// </summary>
    public class CultureUrlFilter : IUrlFilter
    {
        public void Apply(UrlPolicy policy, HttpContext httpContext)
        {
            if (policy.Endpoint == null)
            {
                return;
            }

            if (!policy.LocalizationSettings.SeoFriendlyUrlsForLanguagesEnabled || !HttpMethods.IsGet(policy.Method))
            {
                // Handle only GET requests and when config says so.
                return;
            }

            var localizedRouteMetadata = policy.Endpoint.Metadata.OfType<LocalizedRouteMetadata>().FirstOrDefault();
            if (localizedRouteMetadata == null)
            {
                // Handle only localizable routes
                return;
            }

            var workingLanguage = policy.WorkingLanguage;
            var invalidBehavior = policy.LocalizationSettings.InvalidLanguageRedirectBehaviour;
            var defaultBehavior = policy.LocalizationSettings.DefaultLanguageRedirectBehaviour;

            if (policy.Culture.HasValue)
            {
                var languageService = httpContext.RequestServices.GetService<ILanguageService>();
                if (!languageService.IsPublishedLanguage(policy.Culture))
                {
                    // Language is not defined in system or not assigned to store
                    if (invalidBehavior == InvalidLanguageRedirectBehaviour.ReturnHttp404)
                    {
                        var cultureCodeReplacement = defaultBehavior == DefaultLanguageRedirectBehaviour.PrependSeoCodeAndRedirect
                            ? workingLanguage.GetTwoLetterISOLanguageName()
                            : string.Empty;

                        policy.Culture.Modify(cultureCodeReplacement);
                        policy.IsInvalidUrl = true;
                    }
                    else if (invalidBehavior == InvalidLanguageRedirectBehaviour.FallbackToWorkingLanguage)
                    {
                        policy.Culture.Modify(defaultBehavior == DefaultLanguageRedirectBehaviour.StripSeoCode
                            ? string.Empty
                            : workingLanguage.GetTwoLetterISOLanguageName());
                    }
                }
                else // Not a published language
                {
                    // Redirect default language (if desired)
                    if (policy.Culture == policy.DefaultCultureCode && defaultBehavior == DefaultLanguageRedirectBehaviour.StripSeoCode)
                    {
                        policy.Culture.Strip();
                    }
                }
            }
            else // No culture present
            {
                // Keep default language prefixless (if desired)
                if (!(workingLanguage.UniqueSeoCode == policy.DefaultCultureCode && (int)(defaultBehavior) > 0))
                {
                    // Add language code to URL
                    policy.Culture.Modify(workingLanguage.UniqueSeoCode);
                }
            }
        }
    }
}