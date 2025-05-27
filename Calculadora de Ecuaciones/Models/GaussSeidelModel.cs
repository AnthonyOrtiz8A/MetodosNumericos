using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Calculadora_de_Ecuaciones.Models
{
    public class GaussSeidelModel
    {
        public int GrupoId { get; set; }
        public double[,] MatrizCoeficientes { get; set; } // Matriz A
        public double[] VectorIndependiente { get; set; } // Vector b
        public double[] AproximacionInicial { get; set; } // Vector x0
        public int MaxIteraciones { get; set; }
        public double Tolerancia { get; set; }
        public List<IteracionGaussSeidel> Iteraciones { get; set; } = new List<IteracionGaussSeidel>();
        public string Mensaje { get; set; }
        public string TipoMensaje { get; set; }
        public double[] Solucion { get; set; }
        public int Dimension { get; set; } // Tamaño del sistema
        public DateTime Fecha { get; set; }
        public int UsuarioId { get; set; }

        public List<List<double>> MatrizInput { get; set; } = new List<List<double>>();
        public List<double> VectorBInput { get; set; } = new List<double>();
        public List<double> AproximacionInicialInput { get; set; } = new List<double>();
        public Login Id { get; set; }
    }
    public class IteracionGaussSeidel
    {
        public int GrupoId { get; set; }
        public int Iteracion { get; set; }
        public double[] Valores { get; set; } // Valores de x en esta iteración
        public double Error { get; set; }
        public Login Id { get; set; }
    }
}

