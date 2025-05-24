using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Calculadora_de_Ecuaciones.Models
{
    public class Login
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Contrasena { get; set; }
        public ICollection<SecanteModel> Secante { get; set; }
        public ICollection<SecanteGrupoViewModel> SecanteGrupoViewModels { get; set; }
        public ICollection<NewtonRaphsonModel> NewtonRaphsonModels { get; set; }
        public ICollection<IteracionNewton> IteracionNewton { get; set; }
        public ICollection<MullerModel> MullerModels { get; set; }
        public ICollection<IteracionMuller> IteracionMuller { get; set; }
    }
}