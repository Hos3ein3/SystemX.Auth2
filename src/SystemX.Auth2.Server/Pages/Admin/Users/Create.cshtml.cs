using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Users;

public sealed class CreateModel : AdminPageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = "";

        [StringLength(100)]
        public string? UserName { get; set; }

        [StringLength(50)]
        public string? PhoneNumber { get; set; }

        [StringLength(100)]
        public string? FirstName { get; set; }

        [StringLength(100)]
        public string? LastName { get; set; }

        [DataType(DataType.Password), StringLength(100)]
        public string? TemporaryPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();
        if (!ModelState.IsValid) return Page();

        var userSvc = Svc<IAdminUserService>();
        var request = new AdminCreateUserRequest(
            string.IsNullOrWhiteSpace(Input.UserName) ? null : Input.UserName,
            Input.Email,
            string.IsNullOrWhiteSpace(Input.PhoneNumber) ? null : Input.PhoneNumber,
            string.IsNullOrWhiteSpace(Input.TemporaryPassword) ? null : Input.TemporaryPassword,
            Input.FirstName,
            Input.LastName
        );

        var (result, response) = await userSvc.CreateAsync(request, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", result.ErrorCode ?? "Failed to create user");
            return Page();
        }

        TempData["Success"] = response?.JoinedProject == true && !response.UserWasCreated
            ? $"Existing identity added to project '{CurrentProject!.Name}'."
            : $"User '{Input.Email}' created successfully.";

        return Redirect($"/admin/p/{ProjectKey}/users/detail?id={response!.UserId}");
    }
}
