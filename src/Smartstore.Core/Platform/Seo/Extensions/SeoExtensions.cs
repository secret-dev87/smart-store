﻿using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Smartstore.Core.Localization;
using Smartstore.Domain;
using Smartstore.Engine;

namespace Smartstore.Core.Seo
{
    public static partial class SeoExtensions
    {
        // TODO: (core) Implement SeoExtensions.GetActiveSlug() for ICategoryNode
        // TODO: (core) Apply IDisplayedEntity to BlogPostTag

        public static string BuildSlug<T>(this T entity, int? languageId = null)
            where T : IDisplayedEntity
        {
            Guard.NotNull(entity, nameof(entity));

            var name = entity.GetDisplayName();
            if (entity is ILocalizedEntity le)
            {
                name = le.GetLocalized(entity.GetDisplayNameMemberName(), name, languageId);
            }

            return SeoHelper.BuildSlug(name);
        }

        /// <summary>
        ///  Gets the seo friendly active url slug for a slug supporting entity.
        /// </summary>
        /// <typeparam name="T">Type of slug supporting entity</typeparam>
        /// <param name="entity">Entity instance</param>
        /// <param name="languageId">Language identifier</param>
        /// <param name="returnDefaultValue">A value indicating whether to return default value (if language specified one is not found)</param>
        /// <param name="ensureTwoPublishedLanguages">A value indicating whether to ensure that we have at least two published languages; otherwise, load only default value</param>
        /// <returns>SEO slug</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetActiveSlug<T>(this T entity,
            int? languageId = null,
            bool returnDefaultValue = true,
            bool ensureTwoPublishedLanguages = true)
            where T : BaseEntity, ISlugSupported
        {
            Guard.NotNull(entity, nameof(entity));

            return EngineContext.Current.Scope.ResolveOptional<LocalizedEntityHelper>()?.GetActiveSlug(
                entity.GetEntityName(),
                entity.Id,
                languageId,
                returnDefaultValue,
                ensureTwoPublishedLanguages);
        }

        /// <summary>
        ///  Gets the seo friendly active url slug for a slug supporting entity.
        /// </summary>
        /// <typeparam name="T">Type of slug supporting entity</typeparam>
        /// <param name="entity">Entity instance</param>
        /// <param name="languageId">Language identifier</param>
        /// <param name="returnDefaultValue">A value indicating whether to return default value (if language specified one is not found)</param>
        /// <param name="ensureTwoPublishedLanguages">A value indicating whether to ensure that we have at least two published languages; otherwise, load only default value</param>
        /// <returns>SEO slug</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task<string> GetActiveSlugAsync<T>(this T entity,
            int? languageId = null,
            bool returnDefaultValue = true,
            bool ensureTwoPublishedLanguages = true)
            where T : BaseEntity, ISlugSupported
        {
            Guard.NotNull(entity, nameof(entity));

            return EngineContext.Current.Scope.ResolveOptional<LocalizedEntityHelper>()?.GetActiveSlugAsync(
                entity.GetEntityName(),
                entity.Id,
                languageId,
                returnDefaultValue,
                ensureTwoPublishedLanguages);
        }
    }
}