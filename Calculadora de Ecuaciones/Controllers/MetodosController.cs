using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Calculadora_de_Ecuaciones.Controllers
{
    public class MetodosController : Controller
    {
        // GET: Metodos
        public ActionResult Index()
        {
            ViewBag.ActivePage = "Inicio";
            return View();
        }        
        
        public ActionResult Integrantes()
        {
            ViewBag.ActivePage = "Integrantes";
            return View();
        }        
        public ActionResult AcercaDe()
        {
            ViewBag.ActivePage = "Acerca";
            return View();
        }
        public ActionResult Metodo()
        {
            ViewBag.ActivePage = "Metodos";
            return View();


        }
    };
}