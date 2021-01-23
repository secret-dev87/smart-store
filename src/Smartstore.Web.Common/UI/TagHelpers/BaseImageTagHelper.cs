﻿using System;
using Humanizer;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Smartstore.Core.Content.Media.Imaging;
using Smartstore.Imaging;

namespace Smartstore.Web.UI.TagHelpers
{
    public abstract class BaseImageTagHelper : BaseMediaTagHelper
    {
        const string SizeAttributeName = "img-size";
        const string WidthAttributeName = "img-width";
        const string HeightAttributeName = "img-height";
        const string ResizeModeAttributeName = "img-resize-mode";
        const string AnchorPosAttributeName = "img-anchor-position";
        const string NoFallbackAttributeName = "img-no-fallback";

        /// <summary>
        /// The max physical size (either width or height) to resize the image to.
        /// </summary>
        [HtmlAttributeName(SizeAttributeName)]
        public int? Size { get; set; }

        /// <summary>
        /// The max physical width to resize the image to.
        /// </summary>
        [HtmlAttributeName(WidthAttributeName)]
        public int? Width { get; set; }

        /// <summary>
        /// The max physical width to resize the image to.
        /// </summary>
        [HtmlAttributeName(HeightAttributeName)]
        public int? Height { get; set; }

        /// <summary>
        /// The resize mode to apply during resizing. Defaults to <see cref="ResizeMode.Max"/>.
        /// </summary>
        [HtmlAttributeName(ResizeModeAttributeName)]
        public ResizeMode? ResizeMode { get; set; }

        /// <summary>
        /// The anchor position for (crop) resizing. Defaults to <see cref="AnchorPosition.Center"/>.
        /// </summary>
        [HtmlAttributeName(AnchorPosAttributeName)]
        public AnchorPosition? AnchorPosition { get; set; }

        /// <summary>
        /// If <c>true</c>, output will be suppressed when url generation fails.
        /// </summary>
        [HtmlAttributeName(NoFallbackAttributeName)]
        public bool NoFallback { get; set; }

        protected override string GenerateMediaUrl()
        {
            return MediaUrlGenerator.GenerateUrl(File, BuildImageQuery().ToQueryString(), Host, !NoFallback);
        }

        protected virtual ProcessImageQuery BuildImageQuery()
        {
            var query = new ProcessImageQuery();

            if (Size > 0)
            {
                query.MaxSize = Size.Value;
            }

            if (Width > 0)
            {
                query.MaxWidth = Width.Value;
            }

            if (Height > 0)
            {
                query.MaxHeight = Height.Value;
            }

            if (ResizeMode.HasValue)
            {
                query.ScaleMode = ResizeMode.Value.ToString().ToLower();
            }

            if (AnchorPosition.HasValue)
            {
                query.AnchorPosition = AnchorPosition.Value.ToString().Kebaberize();
            }

            return query;
        }
    }
}
