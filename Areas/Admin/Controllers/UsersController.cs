using System.Text;
using CourtBookingSystem.Areas.Admin.ViewModels;
using CourtBookingSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Index(string? search = null)
        {
            var query = _userManager.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(u =>
                    (u.Email != null && u.Email.Contains(s)) ||
                    (u.UserName != null && u.UserName.Contains(s)) ||
                    (u.FullName != null && u.FullName.Contains(s)));
            }

            var users = await query.OrderBy(u => u.Email).ToListAsync();
            var list = new List<UserListItemViewModel>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                list.Add(new UserListItemViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    UserName = u.UserName,
                    PhoneNumber = u.PhoneNumber,
                    EmailConfirmed = u.EmailConfirmed,
                    LockedOut = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
                    Roles = string.Join(", ", roles)
                });
            }

            ViewData["Search"] = search;
            return View(list);
        }

        // GET: /Admin/Users/Create
        public async Task<IActionResult> Create()
        {
            return View(new CreateUserViewModel { AvailableRoles = await GetRolesAsync() });
        }

        // POST: /Admin/Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.AvailableRoles = await GetRolesAsync();
                return View(model);
            }

            var user = new ApplicationUser
            {
                    FullName = model.FullName,
                Email = model.Email,
                UserName = model.Email,
                PhoneNumber = model.PhoneNumber,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                model.AvailableRoles = await GetRolesAsync();
                return View(model);
            }

            if (model.SelectedRoles is { Count: > 0 })
                await _userManager.AddToRolesAsync(user, model.SelectedRoles);

            TempData["SuccessMessage"] = $"User '{user.Email}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Users/Edit/{id}
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            return View(new EditUserViewModel
            {
                Id = user.Id,
                FullName = user.FullName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                UserName = user.UserName ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                LockoutEnabled = user.LockoutEnabled
            });
        }

        // POST: /Admin/Users/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditUserViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            var user = await _userManager.FindByIdAsync(model.Id);
            if (user == null) return NotFound();
            user.FullName = model.FullName;
            user.Email = model.Email;
            user.UserName = model.UserName;
            user.PhoneNumber = model.PhoneNumber;
            user.EmailConfirmed = model.EmailConfirmed;
            user.LockoutEnabled = model.LockoutEnabled;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                return View(model);
            }

            TempData["SuccessMessage"] = $"User '{user.Email}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Users/Delete/{id}
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            return View(user);
        }

        // POST: /Admin/Users/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var current = await _userManager.GetUserAsync(User);
            if (current != null && current.Id == user.Id)
            {
                TempData["ErrorMessage"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                TempData["ErrorMessage"] = string.Join("; ", result.Errors.Select(e => e.Description));
            else
                TempData["SuccessMessage"] = $"User '{user.Email}' deleted.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Users/AssignRoles/{id}
        public async Task<IActionResult> AssignRoles(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var excludeRoles = new[] { "SuperAdmin", "Member" }; // Exclude Admin role from assignment
            var allRoles = await _roleManager.Roles.Where(r => r.Name != null && !excludeRoles.Contains(r.Name)).OrderBy(r => r.Name).ToListAsync();
            var userRoles = await _userManager.GetRolesAsync(user);

            return View(new AssignRolesViewModel
            {
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                Roles = allRoles.Select(r => new RoleSelection
                {
                    RoleName = r.Name ?? string.Empty,
                    Selected = userRoles.Contains(r.Name ?? string.Empty)
                }).ToList()
            });
        }

        // POST: /Admin/Users/AssignRoles
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRoles(AssignRolesViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var current = await _userManager.GetRolesAsync(user);
            var selected = model.Roles.Where(r => r.Selected).Select(r => r.RoleName).ToList();

            var toAdd = selected.Except(current).ToList();
            var toRemove = current.Except(selected).ToList();

            if (toAdd.Count > 0)
            {
                var addRes = await _userManager.AddToRolesAsync(user, toAdd);
                if (!addRes.Succeeded)
                {
                    TempData["ErrorMessage"] = string.Join("; ", addRes.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Index));
                }
            }

            if (toRemove.Count > 0)
            {
                var remRes = await _userManager.RemoveFromRolesAsync(user, toRemove);
                if (!remRes.Succeeded)
                {
                    TempData["ErrorMessage"] = string.Join("; ", remRes.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Index));
                }
            }

            TempData["SuccessMessage"] = $"Roles updated for '{user.Email}'.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Users/ResetPassword/{id}
        public async Task<IActionResult> ResetPassword(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            return View(new ResetUserPasswordViewModel
            {
                UserId = user.Id,
                Email = user.Email ?? string.Empty
            });
        }

        // POST: /Admin/Users/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetUserPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                return View(model);
            }

            TempData["SuccessMessage"] = $"Password reset for '{user.Email}'.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/SendPasswordResetLink - admin-initiated "Forgot Password"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendPasswordResetLink(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                TempData["ErrorMessage"] = "User has no email address on file.";
                return RedirectToAction(nameof(Index));
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            // NOTE: This URL targets a public Account/ResetPasswordWithToken action
            // you may need to add. For now, the link is shown so admin can copy/share.
            var callbackUrl = Url.Action(
                action: "ResetPasswordWithToken",
                controller: "Account",
                values: new { area = "", email = user.Email, code },
                protocol: Request.Scheme);

            TempData["SuccessMessage"] = $"Password reset link generated for {user.Email}.";
            TempData["ResetLink"] = callbackUrl;
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<SelectListItem>> GetRolesAsync()
        {
            var excludeRoles = new[] { "SuperAdmin", "Member" }; // Exclude Admin role from assignment
            var roles = await _roleManager.Roles
                .Where(r => r.Name != null && !excludeRoles.Contains(r.Name))
                .OrderBy(r => r.Name)
                .ToListAsync();
            return roles.Select(r => new SelectListItem { Value = r.Name, Text = r.Name }).ToList();
        }
    }
}