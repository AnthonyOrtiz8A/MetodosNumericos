using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Calculadora_de_Ecuaciones.Models
{
    public class NewtonRaphsonModel
    {
        public int GrupoId { get; set; }
        public string Funcion { get; set; }
        public double X0 { get; set; }
        public int MaxIter { get; set; }
        public double Tolerancia { get; set; }
        public int UsuarioId { get; set; } 
        public List<IteracionNewton> Iteraciones { get; set; } = new List<IteracionNewton>();
        public DateTime Fecha { get; set; }
        public string Mensaje { get; set; }
        public string TipoMensaje { get; set; }
        public Login Id { get; set; }

    }

    public class IteracionNewton
    {
        public int Iteracion { get; set; }
        public int GrupoId { get; set; }
        public double X { get; set; }
        public double FX { get; set; }
        public double DFX { get; set; }
        public double NextX { get; set; }
        public double MargenError { get; set; }
        public Login Id { get; set; }


    }

}