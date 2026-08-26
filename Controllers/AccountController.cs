using System.Text;
using CourtBookingSystem.Models;
using CourtBookingSystem.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace CourtBookingSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                // Honor an explicit, local returnUrl first
                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                    return LocalRedirect(model.ReturnUrl);

                // Otherwise, route by role to the proper area's dashboard
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user is not null)
                {
                    if (await _userManager.IsInRoleAsync(user, "Admin"))
                        return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

                    if (await _userManager.IsInRoleAsync(user, "Staff"))
                        return RedirectToAction("Index", "Dashboard", new { area = "Staff" });
                }

                // Default for customers / non-area users
                return LocalRedirect("/");
            }

            ModelState.AddModelError("", "Invalid login attempt.");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        // GET: /Account/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        // POST: /Account/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);

            // Do NOT reveal whether the email exists — always show the same confirmation
            if (user is not null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                var callbackUrl = Url.Action(
                    action: nameof(ResetPasswordWithToken),
                    controller: "Account",
                    values: new { email = user.Email, code },
                    protocol: Request.Scheme);

                // TODO: Replace with IEmailSender in production.
                // For now we display the link on the confirmation page for development convenience.
                TempData["ResetLink"] = callbackUrl;
            }

            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        // GET: /Account/ForgotPasswordConfirmation
        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        // GET: /Account/ResetPasswordWithToken?email=...&code=...
        [HttpGet]
        public IActionResult ResetPasswordWithToken(string? email, string? code)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(code))
                return BadRequest("A reset code and email must be supplied.");

            return View(new ResetPasswordWithTokenViewModel
            {
                Email = email,
                Code = code
            });
        }

        // POST: /Account/ResetPasswordWithToken
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPasswordWithToken(ResetPasswordWithTokenViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByEmailAsync(model.Email);
            // Do NOT reveal whether the user exists
            if (user is null)
                return RedirectToAction(nameof(ResetPasswordConfirmation));

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "The reset link is invalid or has expired.");
                return View(model);
            }

            var result = await _userManager.ResetPasswordAsync(user, token, model.Password);
            if (result.Succeeded)
                return RedirectToAction(nameof(ResetPasswordConfirmation));

            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            return View(model);
        }

        // GET: /Account/ResetPasswordConfirmation
        [HttpGet]
        public IActionResult ResetPasswordConfirmation() => View();
    }
}