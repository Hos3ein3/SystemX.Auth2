using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SystemX.Auth2.Server.Pages.Components;

/// <summary>Styled button with Tailwind CSS and runtime branding support.</summary>
[HtmlTargetElement("a2-button", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2ButtonTagHelper : TagHelper
{
    public string Variant { get; set; } = "primary";
    public string Type { get; set; } = "submit";
    public bool Disabled { get; set; }
    public bool Loading { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", Type);
        if (Disabled || Loading) output.Attributes.SetAttribute("disabled", "disabled");

        var baseClasses = "inline-flex items-center justify-center font-medium rounded-[var(--a2-radius,0.375rem)] px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-offset-2 transition-colors disabled:opacity-50 disabled:cursor-not-allowed";
        var variantClasses = Variant.ToLowerInvariant() switch
        {
            "secondary" => "bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-700 text-[var(--a2-color-text,#111827)] dark:text-gray-100 border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-600 focus:ring-gray-400",
            "danger" => "bg-red-600 text-white hover:bg-red-700 focus:ring-red-500",
            "ghost" => "bg-transparent text-[var(--a2-color-text,#111827)] dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 focus:ring-gray-300",
            _ => "bg-[var(--a2-color-primary,#2563eb)] text-[var(--a2-color-primary-text,#ffffff)] hover:opacity-90 focus:ring-[var(--a2-color-primary,#2563eb)]"
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? $"{baseClasses} {variantClasses}" : $"{baseClasses} {variantClasses} {existingClass}");

        if (Loading)
        {
            output.PreContent.SetHtmlContent("<svg class=\"animate-spin -ml-1 mr-2 h-4 w-4\" fill=\"none\" viewBox=\"0 0 24 24\"><circle class=\"opacity-25\" cx=\"12\" cy=\"12\" r=\"10\" stroke=\"currentColor\" stroke-width=\"4\"></circle><path class=\"opacity-75\" fill=\"currentColor\" d=\"M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z\"></path></svg>");
        }
    }
}

/// <summary>Styled anchor link styled as a button.</summary>
[HtmlTargetElement("a2-link-button", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2LinkButtonTagHelper : TagHelper
{
    public string Variant { get; set; } = "primary";
    public string? Href { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        if (!string.IsNullOrEmpty(Href)) output.Attributes.SetAttribute("href", Href);

        var baseClasses = "inline-flex items-center justify-center font-medium rounded-[var(--a2-radius,0.375rem)] px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-offset-2 transition-colors text-center";
        var variantClasses = Variant.ToLowerInvariant() switch
        {
            "secondary" => "bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-700 text-[var(--a2-color-text,#111827)] dark:text-gray-100 border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-600 focus:ring-gray-400",
            "danger" => "bg-red-600 text-white hover:bg-red-700 focus:ring-red-500",
            "ghost" => "bg-transparent text-[var(--a2-color-text,#111827)] dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 focus:ring-gray-300",
            _ => "bg-[var(--a2-color-primary,#2563eb)] text-[var(--a2-color-primary-text,#ffffff)] hover:opacity-90 focus:ring-[var(--a2-color-primary,#2563eb)]"
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? $"{baseClasses} {variantClasses}" : $"{baseClasses} {variantClasses} {existingClass}");
    }
}

/// <summary>Form field wrapper with label, hint, and error message.</summary>
[HtmlTargetElement("a2-field", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2FieldTagHelper : TagHelper
{
    public string? Label { get; set; }
    public string? Hint { get; set; }
    public string? Error { get; set; }
    public bool Required { get; set; }
    public string? For { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? "mb-4" : $"mb-4 {existingClass}");

        var childContent = await output.GetChildContentAsync();
        output.Content.Clear();

        if (!string.IsNullOrEmpty(Label))
        {
            var forAttr = !string.IsNullOrEmpty(For) ? $" for=\"{HtmlEncoder.Default.Encode(For)}\"" : "";
            var reqMarker = Required ? " <span class=\"text-red-500\" aria-hidden=\"true\">*</span>" : "";
            output.Content.AppendHtml($"<label{forAttr} class=\"block text-sm font-medium text-[var(--a2-color-text,#374151)] dark:text-gray-300 mb-1\">{HtmlEncoder.Default.Encode(Label)}{reqMarker}</label>");
        }

        output.Content.AppendHtml(childContent);

        if (!string.IsNullOrEmpty(Hint))
        {
            output.Content.AppendHtml($"<p class=\"mt-1 text-xs text-[var(--a2-color-muted,#6b7280)] dark:text-gray-400\">{HtmlEncoder.Default.Encode(Hint)}</p>");
        }

        if (!string.IsNullOrEmpty(Error))
        {
            output.Content.AppendHtml($"<p class=\"mt-1 text-xs text-red-600 dark:text-red-400\" aria-live=\"polite\">{HtmlEncoder.Default.Encode(Error)}</p>");
        }
    }
}

/// <summary>Styled input element.</summary>
[HtmlTargetElement("a2-input", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2InputTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string Type { get; set; } = "text";
    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public string? Autocomplete { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "input";
        output.TagMode = TagMode.SelfClosing;
        output.Attributes.SetAttribute("type", Type);
        if (!string.IsNullOrEmpty(Name)) output.Attributes.SetAttribute("name", Name);
        if (!string.IsNullOrEmpty(Id)) output.Attributes.SetAttribute("id", Id);
        if (!string.IsNullOrEmpty(Value)) output.Attributes.SetAttribute("value", Value);
        if (!string.IsNullOrEmpty(Placeholder)) output.Attributes.SetAttribute("placeholder", Placeholder);
        if (!string.IsNullOrEmpty(Autocomplete)) output.Attributes.SetAttribute("autocomplete", Autocomplete);
        if (Required) output.Attributes.SetAttribute("required", "required");
        if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");

        var baseClasses = "block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-800 px-3 py-2 text-sm text-[var(--a2-color-text,#111827)] dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)] disabled:bg-gray-100 dark:disabled:bg-gray-900 disabled:opacity-60";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClasses : $"{baseClasses} {existingClass}");
    }
}

/// <summary>Password input with show/hide toggle.</summary>
[HtmlTargetElement("a2-password", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2PasswordTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public string? Autocomplete { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "relative rounded-[var(--a2-radius,0.375rem)]");

        var fieldId = Id ?? Name ?? "password_" + Guid.NewGuid().ToString("N")[..8];
        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var valAttr = !string.IsNullOrEmpty(Value) ? $" value=\"{HtmlEncoder.Default.Encode(Value)}\"" : "";
        var phAttr = !string.IsNullOrEmpty(Placeholder) ? $" placeholder=\"{HtmlEncoder.Default.Encode(Placeholder)}\"" : "";
        var reqAttr = Required ? " required" : "";
        var autoAttr = !string.IsNullOrEmpty(Autocomplete) ? $" autocomplete=\"{HtmlEncoder.Default.Encode(Autocomplete)}\"" : " autocomplete=\"current-password\"";

        output.Content.SetHtmlContent(
            $"<input type=\"password\" id=\"{fieldId}\"{nameAttr}{valAttr}{phAttr}{reqAttr}{autoAttr} class=\"block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-800 px-3 py-2 pr-10 text-sm text-[var(--a2-color-text,#111827)] dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)]\" />" +
            $"<button type=\"button\" data-toggle-password=\"{fieldId}\" class=\"absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 dark:text-gray-500 hover:text-gray-600 dark:hover:text-gray-300 focus:outline-none\" aria-label=\"Toggle password visibility\">" +
            "<svg class=\"h-5 w-5\" fill=\"none\" stroke=\"currentColor\" viewBox=\"0 0 24 24\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M15 12a3 3 0 11-6 0 3 3 0 016 0z\"></path><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z\"></path></svg>" +
            "</button>");
    }
}

/// <summary>Alert banner for messages.</summary>
[HtmlTargetElement("a2-alert", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2AlertTagHelper : TagHelper
{
    public string Variant { get; set; } = "info";
    public bool Dismissible { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("role", "alert");

        var classes = Variant.ToLowerInvariant() switch
        {
            "success" => "bg-green-50 dark:bg-green-950/40 border-l-4 border-green-500 text-green-800 dark:text-green-300 p-4 rounded-r text-sm mb-4",
            "warning" => "bg-yellow-50 dark:bg-yellow-950/40 border-l-4 border-yellow-500 text-yellow-800 dark:text-yellow-300 p-4 rounded-r text-sm mb-4",
            "error" => "bg-red-50 dark:bg-red-950/40 border-l-4 border-red-500 text-red-800 dark:text-red-300 p-4 rounded-r text-sm mb-4",
            _ => "bg-blue-50 dark:bg-blue-950/40 border-l-4 border-blue-500 text-blue-800 dark:text-blue-300 p-4 rounded-r text-sm mb-4"
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? classes : $"{classes} {existingClass}");

        var child = await output.GetChildContentAsync();
        output.Content.SetHtmlContent(child);
    }
}

/// <summary>Card container with header and body.</summary>
[HtmlTargetElement("a2-card", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2CardTagHelper : TagHelper
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseCardClasses = "bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-800 shadow rounded-[var(--a2-radius,0.375rem)] border border-gray-200 dark:border-gray-700 overflow-hidden mb-6";
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseCardClasses : $"{baseCardClasses} {existingClass}");

        var child = await output.GetChildContentAsync();
        output.Content.Clear();

        if (!string.IsNullOrEmpty(Title))
        {
            output.Content.AppendHtml("<div class=\"px-6 py-4 border-b border-gray-200 dark:border-gray-700\">");
            output.Content.AppendHtml($"<h3 class=\"text-lg font-semibold text-[var(--a2-color-text,#111827)] dark:text-gray-100\">{HtmlEncoder.Default.Encode(Title)}</h3>");
            if (!string.IsNullOrEmpty(Subtitle))
            {
                output.Content.AppendHtml($"<p class=\"mt-1 text-sm text-[var(--a2-color-muted,#6b7280)] dark:text-gray-400\">{HtmlEncoder.Default.Encode(Subtitle)}</p>");
            }
            output.Content.AppendHtml("</div>");
        }

        output.Content.AppendHtml("<div class=\"p-6\">");
        output.Content.AppendHtml(child);
        output.Content.AppendHtml("</div>");
    }
}

/// <summary>Badge status pill.</summary>
[HtmlTargetElement("a2-badge", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2BadgeTagHelper : TagHelper
{
    public string Variant { get; set; } = "neutral";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var classes = Variant.ToLowerInvariant() switch
        {
            "success" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 dark:bg-green-900/40 text-green-800 dark:text-green-300",
            "warning" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 dark:bg-yellow-900/40 text-yellow-800 dark:text-yellow-300",
            "error" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 dark:bg-red-900/40 text-red-800 dark:text-red-300",
            "info" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 dark:bg-blue-900/40 text-blue-800 dark:text-blue-300",
            _ => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300"
        };
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? classes : $"{classes} {existingClass}");
    }
}

/// <summary>Table sort header link.</summary>
[HtmlTargetElement("a2-sort-header", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2SortHeaderTagHelper : TagHelper
{
    public string Field { get; set; } = "";
    public string Label { get; set; } = "";
    public string? CurrentSort { get; set; }
    public bool Descending { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        var isCurrent = string.Equals(Field, CurrentSort, StringComparison.OrdinalIgnoreCase);
        var nextDesc = isCurrent && !Descending;

        var query = new Dictionary<string, string>();
        foreach (var q in ViewContext.HttpContext.Request.Query)
        {
            if (q.Key != "sort" && q.Key != "desc" && q.Key != "page")
                query[q.Key] = q.Value.ToString();
        }
        query["sort"] = Field;
        if (nextDesc) query["desc"] = "true";

        var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        output.Attributes.SetAttribute("href", $"?{queryString}");
        output.Attributes.SetAttribute("class", "group inline-flex items-center space-x-1 font-semibold text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-white");

        var icon = "";
        if (isCurrent)
        {
            icon = Descending
                ? "<svg class=\"ml-1 h-4 w-4 text-gray-600 dark:text-gray-400\" fill=\"currentColor\" viewBox=\"0 0 20 20\"><path fill-rule=\"evenodd\" d=\"M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z\" clip-rule=\"evenodd\"></path></svg>"
                : "<svg class=\"ml-1 h-4 w-4 text-gray-600 dark:text-gray-400\" fill=\"currentColor\" viewBox=\"0 0 20 20\"><path fill-rule=\"evenodd\" d=\"M14.707 12.707a1 1 0 01-1.414 0L10 9.414l-3.293 3.293a1 1 0 01-1.414-1.414l4-4a1 1 0 011.414 0l4 4a1 1 0 010 1.414z\" clip-rule=\"evenodd\"></path></svg>";
        }

        output.Content.SetHtmlContent($"{HtmlEncoder.Default.Encode(Label)}{icon}");
    }
}

/// <summary>Search box for list filtering.</summary>
[HtmlTargetElement("a2-search", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2SearchTagHelper : TagHelper
{
    public string Name { get; set; } = "q";
    public string? Value { get; set; }
    public string Placeholder { get; set; } = "Search...";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "relative rounded-[var(--a2-radius,0.375rem)] shadow-sm w-72 max-w-full");

        output.Content.SetHtmlContent(
            "<div class=\"pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3\">" +
            "<svg class=\"h-4 w-4 text-gray-400 dark:text-gray-500\" fill=\"none\" stroke=\"currentColor\" viewBox=\"0 0 24 24\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z\"></path></svg>" +
            "</div>" +
            $"<input type=\"search\" name=\"{HtmlEncoder.Default.Encode(Name)}\" value=\"{HtmlEncoder.Default.Encode(Value ?? "")}\" placeholder=\"{HtmlEncoder.Default.Encode(Placeholder)}\" class=\"block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 pl-10 pr-3 py-2 text-sm text-[var(--a2-color-text,#111827)] dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)]\" />");
    }
}

/// <summary>Empty state display when no rows match.</summary>
[HtmlTargetElement("a2-empty-state", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2EmptyStateTagHelper : TagHelper
{
    public string Title { get; set; } = "No items found";
    public string? Message { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "text-center py-12 px-4");

        var child = await output.GetChildContentAsync();
        output.Content.Clear();

        output.Content.AppendHtml(
            "<svg class=\"mx-auto h-12 w-12 text-gray-400 dark:text-gray-500\" fill=\"none\" viewBox=\"0 0 24 24\" stroke=\"currentColor\">" +
            "<path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"1\" d=\"M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4\"></path>" +
            "</svg>");
        output.Content.AppendHtml($"<h3 class=\"mt-2 text-sm font-semibold text-gray-900 dark:text-gray-100\">{HtmlEncoder.Default.Encode(Title)}</h3>");
        if (!string.IsNullOrEmpty(Message))
        {
            output.Content.AppendHtml($"<p class=\"mt-1 text-sm text-gray-500 dark:text-gray-400\">{HtmlEncoder.Default.Encode(Message)}</p>");
        }
        if (!child.IsEmptyOrWhiteSpace)
        {
            output.Content.AppendHtml("<div class=\"mt-6\">");
            output.Content.AppendHtml(child);
            output.Content.AppendHtml("</div>");
        }
    }
}

/// <summary>One-time secret display box with copy button.</summary>
[HtmlTargetElement("a2-secret-once", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2SecretOnceTagHelper : TagHelper
{
    public string Secret { get; set; } = "";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "rounded-[var(--a2-radius,0.375rem)] bg-amber-50 dark:bg-amber-950/40 border border-amber-300 dark:border-amber-700 p-4 mb-4");

        output.Content.SetHtmlContent(
            "<div class=\"flex items-start\">" +
            "<div class=\"flex-shrink-0\">" +
            "<svg class=\"h-5 w-5 text-amber-500\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z\" clip-rule=\"evenodd\"></path></svg>" +
            "</div>" +
            "<div class=\"ml-3 w-full\">" +
            "<h4 class=\"text-sm font-medium text-amber-800 dark:text-amber-300\">Copy your secret now</h4>" +
            "<p class=\"mt-1 text-xs text-amber-700 dark:text-amber-400\">This secret will never be shown again. Store it securely.</p>" +
            "<div class=\"mt-2 flex rounded-md shadow-sm\">" +
            $"<input type=\"text\" readonly value=\"{HtmlEncoder.Default.Encode(Secret)}\" id=\"secretOnceVal\" class=\"block w-full rounded-none rounded-l-md border border-amber-300 dark:border-amber-700 bg-white dark:bg-gray-900 font-mono text-sm px-3 py-2 text-gray-900 dark:text-gray-100 focus:outline-none\" />" +
            "<button type=\"button\" data-copy=\"#secretOnceVal\" class=\"inline-flex items-center px-4 py-2 border border-l-0 border-amber-300 dark:border-amber-700 rounded-r-md bg-amber-200 dark:bg-amber-800 text-sm font-medium text-amber-900 dark:text-amber-100 hover:bg-amber-300 dark:hover:bg-amber-700 focus:outline-none cursor-pointer\">Copy</button>" +
            "</div>" +
            "</div>" +
            "</div>");
    }
}

/// <summary>Styled select element.</summary>
[HtmlTargetElement("a2-select", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2SelectTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "select";
        output.TagMode = TagMode.StartTagAndEndTag;
        if (!string.IsNullOrEmpty(Name)) output.Attributes.SetAttribute("name", Name);
        if (!string.IsNullOrEmpty(Id)) output.Attributes.SetAttribute("id", Id);
        if (Required) output.Attributes.SetAttribute("required", "required");
        if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");

        var baseClasses = "block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-800 px-3 py-2 text-sm text-[var(--a2-color-text,#111827)] dark:text-gray-100 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)] disabled:bg-gray-100 dark:disabled:bg-gray-900 disabled:opacity-60";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClasses : $"{baseClasses} {existingClass}");

        var child = await output.GetChildContentAsync();
        output.Content.SetHtmlContent(child);
    }
}

/// <summary>Styled custom checkbox with label and hint matching UI Showcase.</summary>
[HtmlTargetElement("a2-checkbox", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2CheckboxTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public bool Checked { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool Disabled { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "label";
        output.TagMode = TagMode.StartTagAndEndTag;
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "group flex items-start gap-3 cursor-pointer select-none" + (string.IsNullOrEmpty(existingClass) || !existingClass.Contains("mb-") ? " mb-3" : "") + (Disabled ? " opacity-60 cursor-not-allowed" : "");
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");

        var boxId = Id ?? (Name != null ? Name.Replace(".", "_") : "chk_" + Guid.NewGuid().ToString("N")[..8]);
        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var chkAttr = Checked ? " checked" : "";
        var disAttr = Disabled ? " disabled" : "";

        var html = $"<div class=\"relative flex items-center justify-center mt-0.5\">" +
                   $"<input id=\"{boxId}\"{nameAttr} type=\"checkbox\" value=\"true\"{chkAttr}{disAttr} class=\"peer sr-only\" />" +
                   (!string.IsNullOrEmpty(Name) && !Disabled ? $"<input type=\"hidden\"{nameAttr} value=\"false\" />" : "") +
                   $"<div class=\"h-5 w-5 rounded border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 peer-checked:bg-[var(--a2-color-primary,#2563eb)] peer-checked:border-[var(--a2-color-primary,#2563eb)] peer-focus:ring-2 peer-focus:ring-blue-500/30 transition-all flex items-center justify-center shadow-xs\">" +
                   $"<svg class=\"h-3.5 w-3.5 text-white opacity-0 peer-checked:opacity-100 transition-opacity\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z\" clip-rule=\"evenodd\" /></svg>" +
                   $"</div>" +
                   $"</div>";

        if (!string.IsNullOrEmpty(Label) || !string.IsNullOrEmpty(Description))
        {
            html += $"<div>";
            if (!string.IsNullOrEmpty(Label))
                html += $"<span class=\"block text-sm font-medium text-gray-900 dark:text-gray-100 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors\">{HtmlEncoder.Default.Encode(Label)}</span>";
            if (!string.IsNullOrEmpty(Description))
                html += $"<span class=\"block text-xs text-gray-500 dark:text-gray-400\">{HtmlEncoder.Default.Encode(Description)}</span>";
            html += "</div>";
        }

        output.Content.SetHtmlContent(html);
    }
}

/// <summary>Styled custom radio button matching UI Showcase.</summary>
[HtmlTargetElement("a2-radio", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2RadioTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public bool Checked { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool Disabled { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "label";
        output.TagMode = TagMode.StartTagAndEndTag;
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "group flex items-start gap-3 cursor-pointer select-none" + (string.IsNullOrEmpty(existingClass) || !existingClass.Contains("mb-") ? " mb-3" : "") + (Disabled ? " opacity-60 cursor-not-allowed" : "");
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");

        var radioId = Id ?? (Name != null && Value != null ? $"{Name}_{Value}".Replace(".", "_") : "radio_" + Guid.NewGuid().ToString("N")[..8]);
        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var valAttr = !string.IsNullOrEmpty(Value) ? $" value=\"{HtmlEncoder.Default.Encode(Value)}\"" : "";
        var chkAttr = Checked ? " checked" : "";
        var disAttr = Disabled ? " disabled" : "";

        var html = $"<div class=\"relative flex items-center justify-center mt-0.5\">" +
                   $"<input id=\"{radioId}\"{nameAttr}{valAttr} type=\"radio\"{chkAttr}{disAttr} class=\"peer sr-only\" />" +
                   $"<div class=\"h-5 w-5 rounded-full border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 peer-checked:border-[var(--a2-color-primary,#2563eb)] peer-focus:ring-2 peer-focus:ring-blue-500/30 transition-all flex items-center justify-center shadow-xs\">" +
                   $"<div class=\"h-2.5 w-2.5 rounded-full bg-[var(--a2-color-primary,#2563eb)] opacity-0 peer-checked:opacity-100 transition-opacity\"></div>" +
                   $"</div>" +
                   $"</div>";

        if (!string.IsNullOrEmpty(Label) || !string.IsNullOrEmpty(Description))
        {
            html += $"<div>";
            if (!string.IsNullOrEmpty(Label))
                html += $"<span class=\"block text-sm font-medium text-gray-900 dark:text-gray-100 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors\">{HtmlEncoder.Default.Encode(Label)}</span>";
            if (!string.IsNullOrEmpty(Description))
                html += $"<span class=\"block text-xs text-gray-500 dark:text-gray-400\">{HtmlEncoder.Default.Encode(Description)}</span>";
            html += "</div>";
        }

        output.Content.SetHtmlContent(html);
    }
}

/// <summary>Styled textarea element.</summary>
[HtmlTargetElement("a2-textarea", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2TextareaTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public int Rows { get; set; } = 3;
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "textarea";
        output.TagMode = TagMode.StartTagAndEndTag;
        if (!string.IsNullOrEmpty(Name)) output.Attributes.SetAttribute("name", Name);
        if (!string.IsNullOrEmpty(Id)) output.Attributes.SetAttribute("id", Id);
        output.Attributes.SetAttribute("rows", Rows);
        if (!string.IsNullOrEmpty(Placeholder)) output.Attributes.SetAttribute("placeholder", Placeholder);
        if (Required) output.Attributes.SetAttribute("required", "required");
        if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");

        var baseClasses = "block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-[var(--a2-color-surface,#ffffff)] dark:bg-gray-800 px-3 py-2 text-sm text-[var(--a2-color-text,#111827)] dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)] disabled:bg-gray-100 dark:disabled:bg-gray-900 disabled:opacity-60";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClasses : $"{baseClasses} {existingClass}");

        if (!string.IsNullOrEmpty(Value)) output.Content.SetContent(Value);
    }
}

/// <summary>Pagination controls matching Section 7 of the work package.</summary>
[HtmlTargetElement("a2-pager", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2PagerTagHelper : TagHelper
{
    [HtmlAttributeName("page")]
    public int Page { get; set; } = 1;

    [HtmlAttributeName("current-page")]
    public int CurrentPageKebab { set => Page = value; }

    [HtmlAttributeName("currentPage")]
    public int CurrentPage { set => Page = value; }

    [HtmlAttributeName("pageSize")]
    public int PageSize { get; set; } = 25;

    [HtmlAttributeName("page-size")]
    public int PageSizeKebab { set => PageSize = value; }

    [HtmlAttributeName("totalCount")]
    public int TotalCount { get; set; }

    [HtmlAttributeName("total-count")]
    public int TotalCountKebab { set => TotalCount = value; }

    [HtmlAttributeName("total-items")]
    public int TotalItemsKebab { set => TotalCount = value; }

    [HtmlAttributeName("totalItems")]
    public int TotalItems { set => TotalCount = value; }

    [HtmlAttributeName("totalPages")]
    public int TotalPages { get; set; }

    [HtmlAttributeName("total-pages")]
    public int TotalPagesKebab { set => TotalPages = value; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "flex items-center justify-between border-t border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 px-4 py-3 sm:px-6 mt-4");

        if (TotalPages <= 0 && PageSize > 0)
        {
            TotalPages = (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
        }

        var startItem = TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        var endItem = Math.Min(Page * PageSize, TotalCount);

        string BuildUrl(int p)
        {
            var query = new Dictionary<string, string>();
            foreach (var q in ViewContext.HttpContext.Request.Query)
            {
                if (!string.Equals(q.Key, "page", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(q.Key, "pageNumber", StringComparison.OrdinalIgnoreCase))
                {
                    query[q.Key] = q.Value.ToString();
                }
            }
            query["page"] = p.ToString();
            query["pageNumber"] = p.ToString();
            return "?" + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"flex flex-col sm:flex-row items-center justify-between gap-3 w-full\">");
        sb.Append($"<div><p class=\"text-sm text-gray-700 dark:text-gray-300\">Showing <span class=\"font-medium text-gray-900 dark:text-gray-100\">{startItem}</span> to <span class=\"font-medium text-gray-900 dark:text-gray-100\">{endItem}</span> of <span class=\"font-medium text-gray-900 dark:text-gray-100\">{TotalCount}</span> results</p></div>");

        sb.Append("<div><nav class=\"isolate inline-flex -space-x-px rounded-md shadow-sm\" aria-label=\"Pagination\">");

        // Previous
        if (Page > 1)
            sb.Append($"<a href=\"{BuildUrl(Page - 1)}\" class=\"relative inline-flex items-center rounded-l-md px-2 py-2 text-gray-400 dark:text-gray-400 ring-1 ring-inset ring-gray-300 dark:ring-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700 focus:z-20 focus:outline-offset-0\"><span class=\"sr-only\">Previous</span>&lsaquo;</a>");
        else
            sb.Append("<span class=\"relative inline-flex items-center rounded-l-md px-2 py-2 text-gray-300 dark:text-gray-600 ring-1 ring-inset ring-gray-300 dark:ring-gray-700 cursor-not-allowed\">&lsaquo;</span>");

        // Page numbers
        int startPage = Math.Max(1, Page - 2);
        int endPage = Math.Min(TotalPages, Page + 2);

        for (int i = startPage; i <= endPage; i++)
        {
            if (i == Page)
                sb.Append($"<span aria-current=\"page\" class=\"relative z-10 inline-flex items-center bg-[var(--a2-color-primary,#2563eb)] px-4 py-2 text-sm font-semibold text-white focus:z-20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600\">{i}</span>");
            else
                sb.Append($"<a href=\"{BuildUrl(i)}\" class=\"relative inline-flex items-center px-4 py-2 text-sm font-semibold text-gray-900 dark:text-gray-100 ring-1 ring-inset ring-gray-300 dark:ring-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700 focus:z-20 focus:outline-offset-0\">{i}</a>");
        }

        // Next
        if (Page < TotalPages)
            sb.Append($"<a href=\"{BuildUrl(Page + 1)}\" class=\"relative inline-flex items-center rounded-r-md px-2 py-2 text-gray-400 dark:text-gray-400 ring-1 ring-inset ring-gray-300 dark:ring-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700 focus:z-20 focus:outline-offset-0\"><span class=\"sr-only\">Next</span>&rsaquo;</a>");
        else
            sb.Append("<span class=\"relative inline-flex items-center rounded-r-md px-2 py-2 text-gray-300 dark:text-gray-600 ring-1 ring-inset ring-gray-300 dark:ring-gray-700 cursor-not-allowed\">&rsaquo;</span>");

        sb.Append("</nav></div></div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Page size selector.</summary>
[HtmlTargetElement("a2-page-size", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2PageSizeTagHelper : TagHelper
{
    public int Value { get; set; } = 25;
    public string Name { get; set; } = "pageSize";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "select";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("name", Name);
        output.Attributes.SetAttribute("data-autosubmit", "true");
        output.Attributes.SetAttribute("class", "min-w-[110px] rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 px-3 py-2 text-sm text-gray-700 dark:text-gray-200 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)]");

        var sizes = new[] { 10, 25, 50, 100 };
        var sb = new System.Text.StringBuilder();
        foreach (var s in sizes)
        {
            var sel = s == Value ? " selected" : "";
            sb.Append($"<option value=\"{s}\"{sel}>{s} / page</option>");
        }
        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Searchable combobox single-select component.</summary>
[HtmlTargetElement("a2-combobox", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2ComboboxTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public string Placeholder { get; set; } = "Select an option...";
    public string SearchPlaceholder { get; set; } = "Search options...";
    public bool Disabled { get; set; }
    public bool Required { get; set; }
    public IEnumerable<SelectListItem>? Items { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var boxId = Id ?? Name ?? "combo_" + Guid.NewGuid().ToString("N")[..8];
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "relative a2-combobox w-full";
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");
        output.Attributes.SetAttribute("data-combobox", boxId);

        var child = await output.GetChildContentAsync();
        var childHtml = child.GetContent();

        var options = new List<(string val, string label, bool selected)>();

        if (Items != null)
        {
            foreach (var item in Items)
            {
                var isSel = (Value != null && string.Equals(Value, item.Value, StringComparison.OrdinalIgnoreCase)) || item.Selected;
                options.Add((item.Value ?? item.Text, item.Text, isSel));
            }
        }

        if (!string.IsNullOrWhiteSpace(childHtml))
        {
            var regex = new Regex(@"<option\s+value=""([^""]*)""(?:\s+selected(?:\s*=\s*""[^""]*"")?)?>([^<]*)</option>", RegexOptions.IgnoreCase);
            var matches = regex.Matches(childHtml);
            foreach (Match m in matches)
            {
                var val = m.Groups[1].Value;
                var text = m.Groups[2].Value.Trim();
                var isSel = m.Value.Contains("selected", StringComparison.OrdinalIgnoreCase) || (Value != null && string.Equals(Value, val, StringComparison.OrdinalIgnoreCase));
                options.Add((val, text, isSel));
            }
        }

        var selectedOption = options.FirstOrDefault(o => o.selected);
        if (selectedOption == default && Value != null)
        {
            selectedOption = options.FirstOrDefault(o => string.Equals(o.val, Value, StringComparison.OrdinalIgnoreCase));
        }

        var currentVal = selectedOption != default ? selectedOption.val : (Value ?? "");
        var currentLabel = selectedOption != default ? selectedOption.label : Placeholder;

        var sb = new StringBuilder();
        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var reqAttr = Required ? " required" : "";
        var disAttr = Disabled ? " disabled" : "";

        sb.Append($"<input type=\"hidden\" id=\"{boxId}\"{nameAttr} value=\"{HtmlEncoder.Default.Encode(currentVal)}\"{reqAttr}{disAttr} />");
        sb.Append($"<button type=\"button\" class=\"a2-combobox-trigger flex w-full items-center justify-between rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 cursor-pointer shadow-xs disabled:opacity-60 disabled:cursor-not-allowed\"{disAttr}>");
        sb.Append($"<span class=\"a2-combobox-display font-medium truncate\">{HtmlEncoder.Default.Encode(currentLabel)}</span>");
        sb.Append("<svg class=\"h-4 w-4 text-gray-400 dark:text-gray-500 flex-shrink-0 ml-2\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z\" clip-rule=\"evenodd\" /></svg>");
        sb.Append("</button>");

        sb.Append("<div class=\"a2-combobox-dropdown hidden absolute z-30 mt-1 w-full rounded-md border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-xl p-1 space-y-1\">");
        sb.Append("<div class=\"p-1 border-b border-gray-100 dark:border-gray-700\">");
        sb.Append($"<input type=\"text\" class=\"a2-combobox-search w-full rounded border border-gray-300 dark:border-gray-600 bg-gray-50 dark:bg-gray-900 px-2.5 py-1 text-xs text-gray-900 dark:text-gray-100 placeholder-gray-400 focus:outline-none focus:ring-1 focus:ring-blue-500\" placeholder=\"{HtmlEncoder.Default.Encode(SearchPlaceholder)}\" autocomplete=\"off\" />");
        sb.Append("</div>");

        sb.Append("<ul class=\"a2-combobox-list max-h-48 overflow-y-auto text-sm text-gray-700 dark:text-gray-200 p-0 m-0 list-none\">");
        foreach (var opt in options)
        {
            var isChosen = string.Equals(opt.val, currentVal, StringComparison.OrdinalIgnoreCase);
            sb.Append($"<li data-value=\"{HtmlEncoder.Default.Encode(opt.val)}\" data-label=\"{HtmlEncoder.Default.Encode(opt.label)}\" class=\"a2-combobox-option flex items-center justify-between px-3 py-2 rounded hover:bg-blue-50 dark:hover:bg-blue-900/30 cursor-pointer\">");
            sb.Append($"<span class=\"truncate\">{HtmlEncoder.Default.Encode(opt.label)}</span>");
            if (isChosen)
            {
                sb.Append("<span class=\"text-xs text-blue-600 dark:text-blue-400 font-semibold ml-2\">Selected</span>");
            }
            sb.Append("</li>");
        }
        sb.Append("</ul>");
        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Searchable multi-select tags component.</summary>
[HtmlTargetElement("a2-multiselect", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2MultiSelectTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Values { get; set; } // comma separated
    public string Placeholder { get; set; } = "Search or add tags...";
    public bool Disabled { get; set; }
    public IEnumerable<SelectListItem>? Items { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var boxId = Id ?? Name ?? "multi_" + Guid.NewGuid().ToString("N")[..8];
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "a2-tag-multiselect relative w-full";
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");
        output.Attributes.SetAttribute("data-tag-multiselect", boxId);

        var child = await output.GetChildContentAsync();
        var childHtml = child.GetContent();

        var options = new List<(string val, string label)>();
        if (Items != null)
        {
            foreach (var item in Items)
            {
                options.Add((item.Value ?? item.Text, item.Text));
            }
        }
        if (!string.IsNullOrWhiteSpace(childHtml))
        {
            var regex = new Regex(@"<option\s+value=""([^""]*)""[^>]*>([^<]*)</option>", RegexOptions.IgnoreCase);
            var matches = regex.Matches(childHtml);
            foreach (Match m in matches)
            {
                options.Add((m.Groups[1].Value, m.Groups[2].Value.Trim()));
            }
        }

        var selectedList = (Values ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var sb = new StringBuilder();
        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var disAttr = Disabled ? " disabled" : "";

        sb.Append($"<input type=\"hidden\" id=\"{boxId}\"{nameAttr} value=\"{HtmlEncoder.Default.Encode(string.Join(",", selectedList))}\"{disAttr} />");
        sb.Append($"<div class=\"a2-tag-multiselect-container flex flex-wrap items-center gap-1.5 p-2 rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 min-h-[42px] focus-within:ring-2 focus-within:ring-blue-500 focus-within:border-blue-500 cursor-text shadow-xs{(Disabled ? " opacity-60 cursor-not-allowed" : "")}\">");
        sb.Append("<div class=\"a2-selected-tags flex flex-wrap gap-1.5\">");
        foreach (var val in selectedList)
        {
            var label = options.FirstOrDefault(o => string.Equals(o.val, val, StringComparison.OrdinalIgnoreCase)).label;
            if (string.IsNullOrEmpty(label)) label = val;

            sb.Append($"<span class=\"inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-xs font-medium bg-blue-100 dark:bg-blue-900/50 text-blue-800 dark:text-blue-200\" data-tag-value=\"{HtmlEncoder.Default.Encode(val)}\">");
            sb.Append($"<span>{HtmlEncoder.Default.Encode(label)}</span>");
            if (!Disabled)
            {
                sb.Append("<button type=\"button\" class=\"text-blue-500 hover:text-blue-700 dark:hover:text-blue-100 font-bold ml-1 cursor-pointer\" data-remove-tag>&times;</button>");
            }
            sb.Append("</span>");
        }
        sb.Append("</div>");
        if (!Disabled)
        {
            sb.Append($"<input type=\"text\" class=\"a2-tag-search-input flex-1 min-w-[120px] bg-transparent text-xs text-gray-900 dark:text-gray-100 placeholder-gray-400 focus:outline-none border-none p-1\" placeholder=\"{HtmlEncoder.Default.Encode(Placeholder)}\" autocomplete=\"off\" />");
        }
        sb.Append("</div>");

        sb.Append("<div class=\"a2-tag-dropdown hidden absolute z-30 mt-1 w-full rounded-md border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-xl p-1\">");
        sb.Append("<ul class=\"a2-tag-options-list max-h-48 overflow-y-auto text-xs text-gray-700 dark:text-gray-200 p-0 m-0 list-none\">");
        foreach (var opt in options)
        {
            var isAdded = selectedList.Contains(opt.val, StringComparer.OrdinalIgnoreCase);
            sb.Append($"<li data-value=\"{HtmlEncoder.Default.Encode(opt.val)}\" class=\"px-2.5 py-1.5 rounded hover:bg-blue-50 dark:hover:bg-blue-900/30 cursor-pointer flex justify-between items-center\">");
            sb.Append($"<span>{HtmlEncoder.Default.Encode(opt.label)}</span>");
            if (isAdded)
            {
                sb.Append("<span class=\"text-xs text-blue-500 font-semibold\">Added</span>");
            }
            sb.Append("</li>");
        }
        sb.Append("</ul>");
        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Custom styled calendar date picker component.</summary>
[HtmlTargetElement("a2-datepicker", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2DatePickerTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public string Placeholder { get; set; } = "YYYY-MM-DD";
    public bool Disabled { get; set; }
    public bool Required { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var dpId = Id ?? Name ?? "dp_" + Guid.NewGuid().ToString("N")[..8];
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "relative a2-datepicker w-full max-w-sm";
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");
        output.Attributes.SetAttribute("data-datepicker", dpId);

        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var valAttr = !string.IsNullOrEmpty(Value) ? $" value=\"{HtmlEncoder.Default.Encode(Value)}\"" : "";
        var reqAttr = Required ? " required" : "";
        var disAttr = Disabled ? " disabled" : "";

        var sb = new StringBuilder();
        sb.Append("<div class=\"relative\">");
        sb.Append($"<input type=\"text\" id=\"{dpId}\"{nameAttr}{valAttr} placeholder=\"{HtmlEncoder.Default.Encode(Placeholder)}\" readonly{reqAttr}{disAttr} class=\"a2-datepicker-input block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 pl-10 pr-8 py-2 text-sm text-gray-900 dark:text-gray-100 placeholder-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 cursor-pointer shadow-xs disabled:opacity-60 disabled:cursor-not-allowed\" />");
        sb.Append("<div class=\"pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-gray-400 dark:text-gray-500\">");
        sb.Append("<svg class=\"h-5 w-5\" fill=\"none\" viewBox=\"0 0 24 24\" stroke=\"currentColor\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z\" /></svg>");
        sb.Append("</div>");
        if (!Disabled)
        {
            sb.Append("<button type=\"button\" class=\"a2-datepicker-clear hidden absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 cursor-pointer text-base\">&times;</button>");
        }
        sb.Append("</div>");

        // Calendar Popup Dropdown
        sb.Append("<div class=\"a2-datepicker-popup hidden absolute z-40 mt-1 w-72 rounded-xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-2xl p-3 select-none\">");
        sb.Append("<div class=\"flex items-center justify-between mb-3 px-1\">");
        sb.Append("<button type=\"button\" class=\"a2-dp-prev p-1.5 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700 text-gray-600 dark:text-gray-300 cursor-pointer focus:outline-none\">");
        sb.Append("<svg class=\"h-4 w-4\" fill=\"none\" viewBox=\"0 0 24 24\" stroke=\"currentColor\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M15 19l-7-7 7-7\" /></svg>");
        sb.Append("</button>");
        sb.Append("<span class=\"a2-dp-title text-sm font-bold text-gray-900 dark:text-gray-100\"></span>");
        sb.Append("<button type=\"button\" class=\"a2-dp-next p-1.5 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700 text-gray-600 dark:text-gray-300 cursor-pointer focus:outline-none\">");
        sb.Append("<svg class=\"h-4 w-4\" fill=\"none\" viewBox=\"0 0 24 24\" stroke=\"currentColor\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M9 5l7 7-7 7\" /></svg>");
        sb.Append("</button>");
        sb.Append("</div>");

        sb.Append("<div class=\"grid grid-cols-7 gap-1 text-center text-xs font-semibold text-gray-400 dark:text-gray-500 mb-2\">");
        sb.Append("<span>Su</span><span>Mo</span><span>Tu</span><span>We</span><span>Th</span><span>Fr</span><span>Sa</span>");
        sb.Append("</div>");

        sb.Append("<div class=\"a2-dp-days grid grid-cols-7 gap-1 text-center text-xs\"></div>");

        sb.Append("<div class=\"flex items-center justify-between pt-2.5 mt-2.5 border-t border-gray-100 dark:border-gray-700 text-xs\">");
        sb.Append("<button type=\"button\" class=\"a2-dp-today font-semibold text-blue-600 dark:text-blue-400 hover:underline cursor-pointer\">Today</button>");
        sb.Append("<button type=\"button\" class=\"a2-dp-close text-gray-500 dark:text-gray-400 hover:text-gray-800 dark:hover:text-gray-100 cursor-pointer\">Close</button>");
        sb.Append("</div>");
        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Custom interactive clock and time picker component.</summary>
[HtmlTargetElement("a2-timepicker", TagStructure = TagStructure.NormalOrSelfClosing)]
[HtmlTargetElement("a2-clockpicker", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2TimePickerTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Value { get; set; }
    public string Placeholder { get; set; } = "HH:MM";
    public bool Use24Hours { get; set; }
    public bool Disabled { get; set; }
    public bool Required { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var tpId = Id ?? Name ?? "tp_" + Guid.NewGuid().ToString("N")[..8];
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "relative a2-timepicker w-full max-w-sm";
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");
        output.Attributes.SetAttribute("data-timepicker", tpId);
        output.Attributes.SetAttribute("data-use-24h", Use24Hours ? "true" : "false");

        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var valAttr = !string.IsNullOrEmpty(Value) ? $" value=\"{HtmlEncoder.Default.Encode(Value)}\"" : "";
        var reqAttr = Required ? " required" : "";
        var disAttr = Disabled ? " disabled" : "";

        var sb = new StringBuilder();
        sb.Append("<div class=\"relative\">");
        sb.Append($"<input type=\"text\" id=\"{tpId}\"{nameAttr}{valAttr} placeholder=\"{HtmlEncoder.Default.Encode(Placeholder)}\" readonly{reqAttr}{disAttr} class=\"a2-timepicker-input block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-800 pl-10 pr-8 py-2 text-sm text-gray-900 dark:text-gray-100 placeholder-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 cursor-pointer shadow-xs disabled:opacity-60 disabled:cursor-not-allowed\" />");
        sb.Append("<div class=\"pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-gray-400 dark:text-gray-500\">");
        sb.Append("<svg class=\"h-5 w-5\" fill=\"none\" viewBox=\"0 0 24 24\" stroke=\"currentColor\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z\" /></svg>");
        sb.Append("</div>");
        if (!Disabled)
        {
            sb.Append("<button type=\"button\" class=\"a2-timepicker-clear hidden absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 cursor-pointer text-base\">&times;</button>");
        }
        sb.Append("</div>");

        // Clock Popup Dropdown
        sb.Append("<div class=\"a2-timepicker-popup hidden absolute z-40 mt-1 w-64 rounded-xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-2xl p-4 select-none\">");
        sb.Append("<div class=\"flex items-center justify-between mb-4 pb-3 border-b border-gray-100 dark:border-gray-700\">");
        sb.Append("<div class=\"text-2xl font-extrabold text-gray-900 dark:text-gray-100 font-mono flex items-center\">");
        sb.Append("<button type=\"button\" class=\"a2-tp-btn-hour px-2.5 py-1 rounded-lg bg-blue-50 dark:bg-blue-900/40 text-blue-600 dark:text-blue-300 cursor-pointer focus:outline-none\">12</button>");
        sb.Append("<span class=\"mx-1 text-gray-400\">:</span>");
        sb.Append("<button type=\"button\" class=\"a2-tp-btn-min px-2.5 py-1 rounded-lg bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-200 cursor-pointer focus:outline-none\">00</button>");
        sb.Append("</div>");

        if (!Use24Hours)
        {
            sb.Append("<div class=\"flex rounded-lg border border-gray-300 dark:border-gray-600 p-0.5 bg-gray-50 dark:bg-gray-700/50 text-xs font-bold\">");
            sb.Append("<button type=\"button\" class=\"a2-tp-ampm px-2 py-1 rounded-md bg-blue-600 text-white cursor-pointer\" data-ampm=\"AM\">AM</button>");
            sb.Append("<button type=\"button\" class=\"a2-tp-ampm px-2 py-1 rounded-md text-gray-600 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600 cursor-pointer\" data-ampm=\"PM\">PM</button>");
            sb.Append("</div>");
        }
        sb.Append("</div>");

        // Mode switch tabs
        sb.Append("<div class=\"flex justify-center border-b border-gray-200 dark:border-gray-700 mb-3 pb-1 text-xs font-semibold gap-4\">");
        sb.Append("<button type=\"button\" class=\"a2-tp-tab-hour text-blue-600 dark:text-blue-400 border-b-2 border-blue-600 pb-1 cursor-pointer\">Hours</button>");
        sb.Append("<button type=\"button\" class=\"a2-tp-tab-min text-gray-400 dark:text-gray-500 pb-1 cursor-pointer hover:text-gray-700 dark:hover:text-gray-300\">Minutes</button>");
        sb.Append("</div>");

        // Hours grid (1..12 or 0..23)
        sb.Append("<div class=\"a2-tp-hours-grid grid grid-cols-4 gap-2 text-center text-xs\">");
        var hoursCount = Use24Hours ? 24 : 12;
        var startH = Use24Hours ? 0 : 1;
        for (int h = startH; h < startH + hoursCount; h++)
        {
            var hStr = Use24Hours ? h.ToString("D2") : h.ToString();
            sb.Append($"<button type=\"button\" data-hour=\"{hStr}\" class=\"a2-tp-hour-item py-2 rounded-lg font-medium text-gray-800 dark:text-gray-200 hover:bg-blue-50 dark:hover:bg-blue-900/30 cursor-pointer transition-colors\">{hStr}</button>");
        }
        sb.Append("</div>");

        // Minutes grid (00, 05, 10, ..., 55)
        sb.Append("<div class=\"a2-tp-minutes-grid hidden grid grid-cols-4 gap-2 text-center text-xs\">");
        for (int m = 0; m < 60; m += 5)
        {
            var mStr = m.ToString("D2");
            sb.Append($"<button type=\"button\" data-minute=\"{mStr}\" class=\"a2-tp-minute-item py-2 rounded-lg font-medium text-gray-800 dark:text-gray-200 hover:bg-blue-50 dark:hover:bg-blue-900/30 cursor-pointer transition-colors\">:{mStr}</button>");
        }
        sb.Append("</div>");

        // Footer with Now and Apply
        sb.Append("<div class=\"flex items-center justify-between pt-3 mt-3 border-t border-gray-100 dark:border-gray-700 text-xs\">");
        sb.Append("<button type=\"button\" class=\"a2-tp-now font-semibold text-blue-600 dark:text-blue-400 hover:underline cursor-pointer\">Current Time</button>");
        sb.Append("<div class=\"flex items-center gap-2\">");
        sb.Append("<button type=\"button\" class=\"a2-tp-cancel px-2.5 py-1 text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 cursor-pointer\">Cancel</button>");
        sb.Append("<button type=\"button\" class=\"a2-tp-apply px-3 py-1 rounded-md bg-blue-600 hover:bg-blue-700 text-white font-semibold shadow-xs cursor-pointer\">Apply</button>");
        sb.Append("</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Styled animated toggle switch component.</summary>
[HtmlTargetElement("a2-toggle", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2ToggleTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public bool Checked { get; set; }
    public bool Disabled { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "label";
        output.TagMode = TagMode.StartTagAndEndTag;

        var switchId = Id ?? (Name != null ? Name.Replace(".", "_") : "toggle_" + Guid.NewGuid().ToString("N")[..8]);
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = "group flex items-center justify-between cursor-pointer py-1 select-none" + (Disabled ? " opacity-60 cursor-not-allowed" : "");
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");

        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var chkAttr = Checked ? " checked" : "";
        var disAttr = Disabled ? " disabled" : "";

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(Label) || !string.IsNullOrEmpty(Description))
        {
            sb.Append("<div>");
            if (!string.IsNullOrEmpty(Label))
                sb.Append($"<span class=\"block text-sm font-medium text-gray-900 dark:text-gray-100 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors\">{HtmlEncoder.Default.Encode(Label)}</span>");
            if (!string.IsNullOrEmpty(Description))
                sb.Append($"<span class=\"block text-xs text-gray-500 dark:text-gray-400\">{HtmlEncoder.Default.Encode(Description)}</span>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"relative flex-shrink-0 ml-4\">");
        sb.Append($"<input id=\"{switchId}\"{nameAttr} type=\"checkbox\" value=\"true\"{chkAttr}{disAttr} class=\"peer sr-only\" />");
        if (!string.IsNullOrEmpty(Name) && !Disabled)
        {
            sb.Append($"<input type=\"hidden\"{nameAttr} value=\"false\" />");
        }
        sb.Append("<div class=\"w-11 h-6 bg-gray-200 dark:bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-[var(--a2-color-primary,#2563eb)]\"></div>");
        sb.Append("</div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Skeleton loading placeholder component.</summary>
[HtmlTargetElement("a2-skeleton", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2SkeletonTagHelper : TagHelper
{
    public string Type { get; set; } = "text"; // text, circle, rect, card, button
    public string? Width { get; set; }
    public string? Height { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var baseClasses = "animate-pulse bg-gray-200 dark:bg-gray-700";
        var typeClasses = Type.ToLowerInvariant() switch
        {
            "circle" => "rounded-full h-10 w-10",
            "rect" => "rounded-lg h-24 w-full",
            "button" => "rounded-md h-9 w-24",
            "card" => "rounded-xl h-40 w-full border border-gray-200 dark:border-gray-700 p-4",
            _ => "rounded h-4 w-full"
        };

        var style = "";
        if (!string.IsNullOrEmpty(Width)) style += $"width: {Width};";
        if (!string.IsNullOrEmpty(Height)) style += $"height: {Height};";
        if (!string.IsNullOrEmpty(style)) output.Attributes.SetAttribute("style", style);

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? $"{baseClasses} {typeClasses}" : $"{baseClasses} {typeClasses} {existingClass}");
    }
}

/// <summary>Loading spinner indicator.</summary>
[HtmlTargetElement("a2-spinner", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2SpinnerTagHelper : TagHelper
{
    public string Size { get; set; } = "md"; // sm, md, lg, xl
    public string Variant { get; set; } = "primary"; // primary, white, gray

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("role", "status");
        output.Attributes.SetAttribute("aria-label", "Loading");

        var sizeClass = Size.ToLowerInvariant() switch
        {
            "sm" => "h-3.5 w-3.5",
            "lg" => "h-8 w-8",
            "xl" => "h-12 w-12",
            _ => "h-5 w-5"
        };

        var colorClass = Variant.ToLowerInvariant() switch
        {
            "white" => "text-white",
            "gray" => "text-gray-500 dark:text-gray-400",
            _ => "text-[var(--a2-color-primary,#2563eb)]"
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", $"inline-block {existingClass}".Trim());

        output.Content.SetHtmlContent($"<svg class=\"animate-spin {sizeClass} {colorClass}\" fill=\"none\" viewBox=\"0 0 24 24\"><circle class=\"opacity-25\" cx=\"12\" cy=\"12\" r=\"10\" stroke=\"currentColor\" stroke-width=\"4\"></circle><path class=\"opacity-75\" fill=\"currentColor\" d=\"M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z\"></path></svg>");
    }
}

/// <summary>Toast notification message component.</summary>
[HtmlTargetElement("a2-toast", TagStructure = TagStructure.NormalOrSelfClosing)]
public class A2ToastTagHelper : TagHelper
{
    public string Variant { get; set; } = "info"; // success, error, warning, info
    public string? Title { get; set; }
    public bool Dismissible { get; set; } = true;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        var (border, bg, iconSvg) = Variant.ToLowerInvariant() switch
        {
            "success" => ("border-emerald-500", "bg-emerald-50 dark:bg-emerald-950/40 text-emerald-800 dark:text-emerald-200", "<svg class=\"h-5 w-5 text-emerald-500 flex-shrink-0\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586l7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z\" clip-rule=\"evenodd\" /></svg>"),
            "error" => ("border-red-500", "bg-red-50 dark:bg-red-950/40 text-red-800 dark:text-red-200", "<svg class=\"h-5 w-5 text-red-500 flex-shrink-0\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z\" clip-rule=\"evenodd\" /></svg>"),
            "warning" => ("border-amber-500", "bg-amber-50 dark:bg-amber-950/40 text-amber-800 dark:text-amber-200", "<svg class=\"h-5 w-5 text-amber-500 flex-shrink-0\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z\" clip-rule=\"evenodd\" /></svg>"),
            _ => ("border-blue-500", "bg-blue-50 dark:bg-blue-950/40 text-blue-800 dark:text-blue-200", "<svg class=\"h-5 w-5 text-blue-500 flex-shrink-0\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z\" clip-rule=\"evenodd\" /></svg>")
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        var baseClass = $"flex items-start gap-3 p-3.5 rounded-lg border-l-4 shadow-md {border} {bg}";
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClass : $"{baseClass} {existingClass}");

        var child = await output.GetChildContentAsync();
        var sb = new StringBuilder();
        sb.Append(iconSvg);
        sb.Append("<div class=\"flex-1 text-xs\">");
        if (!string.IsNullOrEmpty(Title))
        {
            sb.Append($"<h5 class=\"font-bold mb-0.5\">{HtmlEncoder.Default.Encode(Title)}</h5>");
        }
        sb.Append(child.GetContent());
        sb.Append("</div>");
        if (Dismissible)
        {
            sb.Append("<button type=\"button\" data-dismiss-alert class=\"text-current opacity-60 hover:opacity-100 cursor-pointer\">&times;</button>");
        }

        output.Content.SetHtmlContent(sb.ToString());
    }
}
