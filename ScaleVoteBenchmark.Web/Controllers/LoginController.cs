using Microsoft.AspNetCore.Mvc;
using ScaleVoteBenchmark.Web.Services;

namespace ScaleVoteBenchmark.Web.Controllers
{
    public class LoginController : Controller
    {
        private readonly IApi api;

        public LoginController(IApi api)
        {
            this.api = api;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(string username, string password)
        {
            string? token = await api.Login(username, password);

            if (token == null)
            {
                ViewBag.Error = "Neispravno korisničko ime ili lozinka.";
                return View();
            }

            // JWT token sprema se u kolačić kako bi ga korisnik mogao
            // proslijediti prilikom idućih zahtjeva prema admin stranici.
            Response.Cookies.Append("jwt_token", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddHours(1)
            });

            return RedirectToAction("Index", "Admin");
        }

        [HttpPost]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("jwt_token");
            return RedirectToAction(nameof(Index));
        }
    }
}
