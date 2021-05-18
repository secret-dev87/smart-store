﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Smartstore.Web.Rendering;

namespace Smartstore.Web.TagHelpers.Admin
{
    [OutputElementHint("input")]
    [HtmlTargetElement(EditorTagName, Attributes = ForAttributeName, TagStructure = TagStructure.WithoutEndTag)]
    public class EditorTagHelper : BaseFormTagHelper
    {
        const string EditorTagName = "editor";
        const string TemplateAttributeName = "asp-template";
        //const string ValueAttributeName = "asp-value";
        const string PostfixAttributeName = "sm-postfix";

        /// <summary>
        /// Specifies the editor template which will be used to render the field.
        /// </summary>
        [HtmlAttributeName(TemplateAttributeName)]
        public string Template { get; set; }

        /// <summary>
        /// Specifies the value to set into editor input tag.
        /// </summary>
        //[HtmlAttributeName(ValueAttributeName)]
        //public string Value { get; set; }

        /// <summary>
        /// The text which will be displayed inside the input tag as a post fix.
        /// </summary>
        [HtmlAttributeName(PostfixAttributeName)]
        public string Postfix { get; set; }

        private readonly IHtmlHelper _htmlHelper;

        public EditorTagHelper(IHtmlHelper htmlHelper)
        {
            _htmlHelper = htmlHelper;
        }

        protected override void ProcessCore(TagHelperContext context, TagHelperOutput output)
        {
            output.SuppressOutput();

            var htmlAttributes = new Dictionary<string, object>();

            if (Postfix.HasValue())
                htmlAttributes.Add("postfix", Postfix);

            var viewContextAware = _htmlHelper as IViewContextAware;
            viewContextAware?.Contextualize(ViewContext);

            var attrs = output.Attributes;
            if (attrs != null && attrs.Count > 0)
            {
                foreach (var attr in attrs)
                {
                    htmlAttributes.Add(attr.Name, attr.Value);
                }
            }

            output.Content.SetHtmlContent(_htmlHelper.EditorFor(For, Template, new { htmlAttributes }));
        }
    }
}
