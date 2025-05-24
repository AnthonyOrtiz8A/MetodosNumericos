using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Calculadora_de_Ecuaciones.Models
{
    public class SecanteModel
    {
        public int GrupoId { get; set; }
        public string Funcion { get; set; }
        public double X0 { get; set; }
        public double X1 { get; set; }
        public int MaxIter { get; set; }  
        public double Tolerancia { get; set; }
        public int UsuarioId { get; set; }
        public List<ResultadoIteracion> Resultados { get; set; }
        public string Mensaje { get; set; }
        public string TipoMensaje { get; set; }
        public bool ConvergenciaExitosa { get; set; }
        public Login Id {  get; set; }
    }

    public class ResultadoIteracion
    {
        public int GrupoId { get; set; }
        public int Iteracion { get; set; }
        public double Xi { get; set; }
        public double Xi_1 { get; set; }
        public double XiMas1 { get; set; }
        public double Error { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SecanteGrupoViewModel
    {
        public int GrupoId { get; set; }
        public string Funcion { get; set; }
        public double X0 { get; set; }
        public double X1 { get; set; }
        public int MaxIter { get; set; }
        public double Tolerancia { get; set; }
        public DateTime Fecha { get; set; }
        public int UsuarioId { get; set; }
        public List<ResultadoIteracion> Iteraciones { get; set; }
        public Login Id { get; set; }

    }
}