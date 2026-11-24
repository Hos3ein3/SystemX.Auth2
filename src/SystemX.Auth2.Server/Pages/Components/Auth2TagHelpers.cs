using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SystemX.Auth2.Server.Pages.Components;

/// <summary>Styled button with Tailwind CSS and runtime branding support.</summary>
[HtmlTargetElement("a2-button")]
public class A2ButtonTagHelper : TagHelper
{
    public string Variant { get; set; } = "primary";
    public string Type { get; set; } = "submit";
    public bool Disabled { get; set; }
    public bool Loading { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "button";
        output.Attributes.SetAttribute("type", Type);
        if (Disabled || Loading) output.Attributes.SetAttribute("disabled", "disabled");

        var baseClasses = "inline-flex items-center justify-center font-medium rounded-[var(--a2-radius,0.375rem)] px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-offset-2 transition-colors disabled:opacity-50 disabled:cursor-not-allowed";
        var variantClasses = Variant.ToLowerInvariant() switch
        {
            "secondary" => "bg-[var(--a2-color-surface,#ffffff)] text-[var(--a2-color-text,#111827)] border border-gray-300 hover:bg-gray-50 focus:ring-gray-400",
            "danger" => "bg-red-600 text-white hover:bg-red-700 focus:ring-red-500",
            "ghost" => "bg-transparent text-[var(--a2-color-text,#111827)] hover:bg-gray-100 focus:ring-gray-300",
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
[HtmlTargetElement("a2-link-button")]
public class A2LinkButtonTagHelper : TagHelper
{
    public string Variant { get; set; } = "primary";
    public string? Href { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        if (!string.IsNullOrEmpty(Href)) output.Attributes.SetAttribute("href", Href);

        var baseClasses = "inline-flex items-center justify-center font-medium rounded-[var(--a2-radius,0.375rem)] px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-offset-2 transition-colors text-center";
        var variantClasses = Variant.ToLowerInvariant() switch
        {
            "secondary" => "bg-[var(--a2-color-surface,#ffffff)] text-[var(--a2-color-text,#111827)] border border-gray-300 hover:bg-gray-50 focus:ring-gray-400",
            "danger" => "bg-red-600 text-white hover:bg-red-700 focus:ring-red-500",
            "ghost" => "bg-transparent text-[var(--a2-color-text,#111827)] hover:bg-gray-100 focus:ring-gray-300",
            _ => "bg-[var(--a2-color-primary,#2563eb)] text-[var(--a2-color-primary-text,#ffffff)] hover:opacity-90 focus:ring-[var(--a2-color-primary,#2563eb)]"
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? $"{baseClasses} {variantClasses}" : $"{baseClasses} {variantClasses} {existingClass}");
    }
}

/// <summary>Form field wrapper with label, hint, and error message.</summary>
[HtmlTargetElement("a2-field")]
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
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? "mb-4" : $"mb-4 {existingClass}");

        var childContent = await output.GetChildContentAsync();
        output.Content.Clear();

        if (!string.IsNullOrEmpty(Label))
        {
            var forAttr = !string.IsNullOrEmpty(For) ? $" for=\"{HtmlEncoder.Default.Encode(For)}\"" : "";
            var reqMarker = Required ? " <span class=\"text-red-500\" aria-hidden=\"true\">*</span>" : "";
            output.Content.AppendHtml($"<label{forAttr} class=\"block text-sm font-medium text-[var(--a2-color-text,#374151)] mb-1\">{HtmlEncoder.Default.Encode(Label)}{reqMarker}</label>");
        }

        output.Content.AppendHtml(childContent);

        if (!string.IsNullOrEmpty(Hint))
        {
            output.Content.AppendHtml($"<p class=\"mt-1 text-xs text-[var(--a2-color-muted,#6b7280)]\">{HtmlEncoder.Default.Encode(Hint)}</p>");
        }

        if (!string.IsNullOrEmpty(Error))
        {
            output.Content.AppendHtml($"<p class=\"mt-1 text-xs text-red-600\" aria-live=\"polite\">{HtmlEncoder.Default.Encode(Error)}</p>");
        }
    }
}

/// <summary>Styled input element.</summary>
[HtmlTargetElement("a2-input")]
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

        var baseClasses = "block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 bg-[var(--a2-color-surface,#ffffff)] px-3 py-2 text-sm text-[var(--a2-color-text,#111827)] placeholder-gray-400 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)] disabled:bg-gray-100 disabled:opacity-60";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClasses : $"{baseClasses} {existingClass}");
    }
}

/// <summary>Password input with show/hide toggle.</summary>
[HtmlTargetElement("a2-password")]
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
            $"<input type=\"password\" id=\"{fieldId}\"{nameAttr}{valAttr}{phAttr}{reqAttr}{autoAttr} class=\"block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 bg-[var(--a2-color-surface,#ffffff)] px-3 py-2 pr-10 text-sm text-[var(--a2-color-text,#111827)] placeholder-gray-400 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)]\" />" +
            $"<button type=\"button\" onclick=\"var el = document.getElementById('{fieldId}'); el.type = el.type === 'password' ? 'text' : 'password';\" class=\"absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600 focus:outline-none\" aria-label=\"Toggle password visibility\">" +
            "<svg class=\"h-5 w-5\" fill=\"none\" stroke=\"currentColor\" viewBox=\"0 0 24 24\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M15 12a3 3 0 11-6 0 3 3 0 016 0z\"></path><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z\"></path></svg>" +
            "</button>");
    }
}

/// <summary>Alert banner for messages.</summary>
[HtmlTargetElement("a2-alert")]
public class A2AlertTagHelper : TagHelper
{
    public string Variant { get; set; } = "info";
    public bool Dismissible { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("role", "alert");

        var classes = Variant.ToLowerInvariant() switch
        {
            "success" => "bg-green-50 border-l-4 border-green-500 text-green-800 p-4 rounded-r text-sm mb-4",
            "warning" => "bg-yellow-50 border-l-4 border-yellow-500 text-yellow-800 p-4 rounded-r text-sm mb-4",
            "error" => "bg-red-50 border-l-4 border-red-500 text-red-800 p-4 rounded-r text-sm mb-4",
            _ => "bg-blue-50 border-l-4 border-blue-500 text-blue-800 p-4 rounded-r text-sm mb-4"
        };

        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? classes : $"{classes} {existingClass}");

        var child = await output.GetChildContentAsync();
        output.Content.SetHtmlContent(child);
    }
}

/// <summary>Card container with header and body.</summary>
[HtmlTargetElement("a2-card")]
public class A2CardTagHelper : TagHelper
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? "bg-[var(--a2-color-surface,#ffffff)] shadow rounded-[var(--a2-radius,0.375rem)] border border-gray-200 overflow-hidden mb-6" : $"bg-[var(--a2-color-surface,#ffffff)] shadow rounded-[var(--a2-radius,0.375rem)] border border-gray-200 overflow-hidden mb-6 {existingClass}");

        var child = await output.GetChildContentAsync();
        output.Content.Clear();

        if (!string.IsNullOrEmpty(Title))
        {
            output.Content.AppendHtml("<div class=\"px-6 py-4 border-b border-gray-200\">");
            output.Content.AppendHtml($"<h3 class=\"text-lg font-semibold text-[var(--a2-color-text,#111827)]\">{HtmlEncoder.Default.Encode(Title)}</h3>");
            if (!string.IsNullOrEmpty(Subtitle))
            {
                output.Content.AppendHtml($"<p class=\"mt-1 text-sm text-[var(--a2-color-muted,#6b7280)]\">{HtmlEncoder.Default.Encode(Subtitle)}</p>");
            }
            output.Content.AppendHtml("</div>");
        }

        output.Content.AppendHtml("<div class=\"p-6\">");
        output.Content.AppendHtml(child);
        output.Content.AppendHtml("</div>");
    }
}

/// <summary>Badge status pill.</summary>
[HtmlTargetElement("a2-badge")]
public class A2BadgeTagHelper : TagHelper
{
    public string Variant { get; set; } = "neutral";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        var classes = Variant.ToLowerInvariant() switch
        {
            "success" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800",
            "warning" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800",
            "error" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800",
            "info" => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800",
            _ => "inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800"
        };
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? classes : $"{classes} {existingClass}");
    }
}

/// <summary>Table sort header link.</summary>
[HtmlTargetElement("a2-sort-header")]
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
        output.Attributes.SetAttribute("class", "group inline-flex items-center space-x-1 font-semibold text-gray-700 hover:text-gray-900");

        var icon = "";
        if (isCurrent)
        {
            icon = Descending
                ? "<svg class=\"ml-1 h-4 w-4 text-gray-600\" fill=\"currentColor\" viewBox=\"0 0 20 20\"><path fill-rule=\"evenodd\" d=\"M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z\" clip-rule=\"evenodd\"></path></svg>"
                : "<svg class=\"ml-1 h-4 w-4 text-gray-600\" fill=\"currentColor\" viewBox=\"0 0 20 20\"><path fill-rule=\"evenodd\" d=\"M14.707 12.707a1 1 0 01-1.414 0L10 9.414l-3.293 3.293a1 1 0 01-1.414-1.414l4-4a1 1 0 011.414 0l4 4a1 1 0 010 1.414z\" clip-rule=\"evenodd\"></path></svg>";
        }

        output.Content.SetHtmlContent($"{HtmlEncoder.Default.Encode(Label)}{icon}");
    }
}

/// <summary>Search box for list filtering.</summary>
[HtmlTargetElement("a2-search")]
public class A2SearchTagHelper : TagHelper
{
    public string Name { get; set; } = "q";
    public string? Value { get; set; }
    public string Placeholder { get; set; } = "Search...";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "relative rounded-[var(--a2-radius,0.375rem)] shadow-sm max-w-xs w-full");

        output.Content.SetHtmlContent(
            "<div class=\"pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3\">" +
            "<svg class=\"h-4 w-4 text-gray-400\" fill=\"none\" stroke=\"currentColor\" viewBox=\"0 0 24 24\"><path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"2\" d=\"M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z\"></path></svg>" +
            "</div>" +
            $"<input type=\"search\" name=\"{HtmlEncoder.Default.Encode(Name)}\" value=\"{HtmlEncoder.Default.Encode(Value ?? "")}\" placeholder=\"{HtmlEncoder.Default.Encode(Placeholder)}\" class=\"block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 bg-[var(--a2-color-surface,#ffffff)] pl-10 pr-3 py-2 text-sm text-[var(--a2-color-text,#111827)] placeholder-gray-400 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)]\" />");
    }
}

/// <summary>Empty state display when no rows match.</summary>
[HtmlTargetElement("a2-empty-state")]
public class A2EmptyStateTagHelper : TagHelper
{
    public string Title { get; set; } = "No items found";
    public string? Message { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "text-center py-12 px-4");

        var child = await output.GetChildContentAsync();
        output.Content.Clear();

        output.Content.AppendHtml(
            "<svg class=\"mx-auto h-12 w-12 text-gray-400\" fill=\"none\" viewBox=\"0 0 24 24\" stroke=\"currentColor\">" +
            "<path stroke-linecap=\"round\" stroke-linejoin=\"round\" stroke-width=\"1\" d=\"M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4\"></path>" +
            "</svg>");
        output.Content.AppendHtml($"<h3 class=\"mt-2 text-sm font-semibold text-gray-900\">{HtmlEncoder.Default.Encode(Title)}</h3>");
        if (!string.IsNullOrEmpty(Message))
        {
            output.Content.AppendHtml($"<p class=\"mt-1 text-sm text-gray-500\">{HtmlEncoder.Default.Encode(Message)}</p>");
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
[HtmlTargetElement("a2-secret-once")]
public class A2SecretOnceTagHelper : TagHelper
{
    public string Secret { get; set; } = "";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "rounded-[var(--a2-radius,0.375rem)] bg-amber-50 border border-amber-300 p-4 mb-4");

        output.Content.SetHtmlContent(
            "<div class=\"flex items-start\">" +
            "<div class=\"flex-shrink-0\">" +
            "<svg class=\"h-5 w-5 text-amber-500\" viewBox=\"0 0 20 20\" fill=\"currentColor\"><path fill-rule=\"evenodd\" d=\"M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z\" clip-rule=\"evenodd\"></path></svg>" +
            "</div>" +
            "<div class=\"ml-3 w-full\">" +
            "<h4 class=\"text-sm font-medium text-amber-800\">Copy your secret now</h4>" +
            "<p class=\"mt-1 text-xs text-amber-700\">This secret will never be shown again. Store it securely.</p>" +
            "<div class=\"mt-2 flex rounded-md shadow-sm\">" +
            $"<input type=\"text\" readonly value=\"{HtmlEncoder.Default.Encode(Secret)}\" id=\"secretOnceVal\" class=\"block w-full rounded-none rounded-l-md border-amber-300 bg-white font-mono text-sm px-3 py-2 text-gray-900 focus:outline-none\" />" +
            "<button type=\"button\" onclick=\"navigator.clipboard.writeText(document.getElementById('secretOnceVal').value); this.innerText='Copied!';\" class=\"inline-flex items-center px-4 py-2 border border-l-0 border-amber-300 rounded-r-md bg-amber-200 text-sm font-medium text-amber-900 hover:bg-amber-300 focus:outline-none\">Copy</button>" +
            "</div>" +
            "</div>" +
            "</div>");
    }
}

/// <summary>Styled select element.</summary>
[HtmlTargetElement("a2-select")]
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

        var baseClasses = "block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 bg-[var(--a2-color-surface,#ffffff)] px-3 py-2 text-sm text-[var(--a2-color-text,#111827)] focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)] disabled:bg-gray-100 disabled:opacity-60";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClasses : $"{baseClasses} {existingClass}");

        var child = await output.GetChildContentAsync();
        output.Content.SetHtmlContent(child);
    }
}

/// <summary>Styled checkbox with label and hint.</summary>
[HtmlTargetElement("a2-checkbox")]
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
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "relative flex items-start mb-4");

        var boxId = Id ?? Name ?? "chk_" + Guid.NewGuid().ToString("N")[..8];
        var nameAttr = !string.IsNullOrEmpty(Name) ? $" name=\"{HtmlEncoder.Default.Encode(Name)}\"" : "";
        var chkAttr = Checked ? " checked" : "";
        var disAttr = Disabled ? " disabled" : "";

        var html = $"<div class=\"flex h-5 items-center\">" +
                   $"<input id=\"{boxId}\"{nameAttr} type=\"checkbox\" value=\"true\"{chkAttr}{disAttr} class=\"h-4 w-4 rounded border-gray-300 text-[var(--a2-color-primary,#2563eb)] focus:ring-[var(--a2-color-primary,#2563eb)]\" />" +
                   $"</div>";

        if (!string.IsNullOrEmpty(Label) || !string.IsNullOrEmpty(Description))
        {
            html += $"<div class=\"ml-3 text-sm leading-5\">";
            if (!string.IsNullOrEmpty(Label))
                html += $"<label for=\"{boxId}\" class=\"font-medium text-[var(--a2-color-text,#111827)]\">{HtmlEncoder.Default.Encode(Label)}</label>";
            if (!string.IsNullOrEmpty(Description))
                html += $"<p class=\"text-xs text-[var(--a2-color-muted,#6b7280)]\">{HtmlEncoder.Default.Encode(Description)}</p>";
            html += "</div>";
        }

        output.Content.SetHtmlContent(html);
    }
}

/// <summary>Styled textarea element.</summary>
[HtmlTargetElement("a2-textarea")]
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

        var baseClasses = "block w-full rounded-[var(--a2-radius,0.375rem)] border border-gray-300 bg-[var(--a2-color-surface,#ffffff)] px-3 py-2 text-sm text-[var(--a2-color-text,#111827)] placeholder-gray-400 focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)] disabled:bg-gray-100 disabled:opacity-60";
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existingClass) ? baseClasses : $"{baseClasses} {existingClass}");

        if (!string.IsNullOrEmpty(Value)) output.Content.SetContent(Value);
    }
}

/// <summary>Pagination controls matching Section 7 of the work package.</summary>
[HtmlTargetElement("a2-pager")]
public class A2PagerTagHelper : TagHelper
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "flex items-center justify-between border-t border-gray-200 bg-white px-4 py-3 sm:px-6 mt-4");

        var startItem = TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        var endItem = Math.Min(Page * PageSize, TotalCount);

        string BuildUrl(int p)
        {
            var query = new Dictionary<string, string>();
            foreach (var q in ViewContext.HttpContext.Request.Query)
            {
                if (q.Key != "page") query[q.Key] = q.Value.ToString();
            }
            query["page"] = p.ToString();
            return "?" + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"hidden sm:flex sm:flex-1 sm:items-center sm:justify-between\">");
        sb.Append($"<div><p class=\"text-sm text-gray-700\">Showing <span class=\"font-medium\">{startItem}</span> to <span class=\"font-medium\">{endItem}</span> of <span class=\"font-medium\">{TotalCount}</span> results</p></div>");

        sb.Append("<div><nav class=\"isolate inline-flex -space-x-px rounded-md shadow-sm\" aria-label=\"Pagination\">");

        // Previous
        if (Page > 1)
            sb.Append($"<a href=\"{BuildUrl(Page - 1)}\" class=\"relative inline-flex items-center rounded-l-md px-2 py-2 text-gray-400 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 focus:z-20 focus:outline-offset-0\"><span class=\"sr-only\">Previous</span>&lsaquo;</a>");
        else
            sb.Append("<span class=\"relative inline-flex items-center rounded-l-md px-2 py-2 text-gray-300 ring-1 ring-inset ring-gray-300 cursor-not-allowed\">&lsaquo;</span>");

        // Page numbers
        int startPage = Math.Max(1, Page - 2);
        int endPage = Math.Min(TotalPages, Page + 2);

        for (int i = startPage; i <= endPage; i++)
        {
            if (i == Page)
                sb.Append($"<span aria-current=\"page\" class=\"relative z-10 inline-flex items-center bg-[var(--a2-color-primary,#2563eb)] px-4 py-2 text-sm font-semibold text-white focus:z-20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600\">{i}</span>");
            else
                sb.Append($"<a href=\"{BuildUrl(i)}\" class=\"relative inline-flex items-center px-4 py-2 text-sm font-semibold text-gray-900 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 focus:z-20 focus:outline-offset-0\">{i}</a>");
        }

        // Next
        if (Page < TotalPages)
            sb.Append($"<a href=\"{BuildUrl(Page + 1)}\" class=\"relative inline-flex items-center rounded-r-md px-2 py-2 text-gray-400 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 focus:z-20 focus:outline-offset-0\"><span class=\"sr-only\">Next</span>&rsaquo;</a>");
        else
            sb.Append("<span class=\"relative inline-flex items-center rounded-r-md px-2 py-2 text-gray-300 ring-1 ring-inset ring-gray-300 cursor-not-allowed\">&rsaquo;</span>");

        sb.Append("</nav></div></div>");

        output.Content.SetHtmlContent(sb.ToString());
    }
}

/// <summary>Page size selector.</summary>
[HtmlTargetElement("a2-page-size")]
public class A2PageSizeTagHelper : TagHelper
{
    public int Value { get; set; } = 25;
    public string Name { get; set; } = "pageSize";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "select";
        output.Attributes.SetAttribute("name", Name);
        output.Attributes.SetAttribute("onchange", "this.form.submit()");
        output.Attributes.SetAttribute("class", "rounded-[var(--a2-radius,0.375rem)] border border-gray-300 bg-[var(--a2-color-surface,#ffffff)] px-2 py-2 text-sm text-[var(--a2-color-text,#111827)] focus:border-[var(--a2-color-primary,#2563eb)] focus:outline-none focus:ring-1 focus:ring-[var(--a2-color-primary,#2563eb)]");

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
