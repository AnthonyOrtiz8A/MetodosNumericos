using System.Web;
using System.Web.Mvc;

namespace Calculadora_de_Ecuaciones
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }
}
