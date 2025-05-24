using System;
using System.Collections.Generic;

namespace Calculadora_de_Ecuaciones.Models
{
    public class MullerModel
    {
        public int GrupoId { get; set; } 
        public string Funcion { get; set; } 
        public double X0 { get; set; }
        public double X1 { get; set; } 
        public double X2 { get; set; } 
        public int MaxIter { get; set; } 
        public double Tolerancia { get; set; }
        public int UsuarioId { get; set; }
        public List<IteracionMuller> Iteraciones { get; set; } = new List<IteracionMuller>(); 
        public string Mensaje { get; set; } 
        public string TipoMensaje { get; set; } 
        public double? Root { get; set; } 
        public DateTime Fecha { get; set; }
        public Login Id { get; set; }

    }

    public class IteracionMuller
    {
        public int GrupoId { get; set; } 
        public int Iteracion { get; set; } 
        public double X0 { get; set; } 
        public double X1 { get; set; } 
        public double X2 { get; set; } 
        public double A { get; set; } 
        public double B { get; set; } 
        public double C { get; set; } 
        public double NextX { get; set; }
        public double MargenError { get; set; }
        public Login Id { get; set; }

    }
}