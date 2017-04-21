using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace SilverScript.viewmodels
{
    public class HomeController:Controller
    {
        public IActionResult Index()
        {
            return File("~/index.htm", "text/html");

        }
        
    }
}
