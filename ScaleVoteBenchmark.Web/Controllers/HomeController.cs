using Microsoft.AspNetCore.Mvc;
using ScaleVoteBenchmark.Web.Services;

namespace ScaleVoteBenchmark.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IApi api;

        public HomeController(IApi api)
        {
            this.api = api;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Vote(string option)
        {
            await api.VoteAdd(option);
            return RedirectToAction(nameof(Index));
        }
    }
}
