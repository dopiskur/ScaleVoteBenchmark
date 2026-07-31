using Microsoft.AspNetCore.Mvc;
using ScaleVoteBenchmark.Web.Services;

namespace ScaleVoteBenchmark.Web.Controllers
{
    public class AdminController : Controller
    {
        private readonly IApi api;

        public AdminController(IApi api)
        {
            this.api = api;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            string? token = Request.Cookies["jwt_token"];

            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Index", "Login");
            }

            var counts = await api.VoteCountsGet(token);

            if (counts == null)
            {
                // Token je istekao ili nevažeći, korisnik se vraća na prijavu.
                Response.Cookies.Delete("jwt_token");
                return RedirectToAction("Index", "Login");
            }

            return View(counts);
        }

        /// <summary>
        /// Endpoint koji koristi klijentski JavaScript za periodično
        /// osvježavanje rezultata bez ponovnog učitavanja cijele stranice.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Results()
        {
            string? token = Request.Cookies["jwt_token"];

            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            var counts = await api.VoteCountsGet(token);

            if (counts == null)
            {
                return Unauthorized();
            }

            return Json(counts);
        }
    }
}
