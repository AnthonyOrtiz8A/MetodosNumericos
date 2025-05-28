using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Data.SQLite;
using Calculadora_de_Ecuaciones.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using System.Globalization;

namespace Calculadora_de_Ecuaciones.Controllers
{
    public class GaussSeidelController : Controller
    {
        private string GetConnectionString()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "CalculadoraBD.s3db");
            return $"Data Source={dbPath};Version=3;";
        }
        public ActionResult Index(int? n)
        {
            var modelo = new GaussSeidelModel();
            if (n.HasValue && n.Value > 0)
            {
                modelo.Dimension = n.Value;
                modelo.MatrizInput = new List<List<double>>();
                modelo.VectorBInput = new List<double>();
                modelo.AproximacionInicialInput = new List<double>();

                for (int i = 0; i < n.Value; i++)
                {
                    modelo.MatrizInput.Add(new List<double>(new double[n.Value]));
                    modelo.VectorBInput.Add(0);
                    modelo.AproximacionInicialInput.Add(0);
                }
            }

            return View(modelo);
        }
        [HttpPost]
        public ActionResult Calculate(GaussSeidelModel model)
        {
            if (!ModelState.IsValid || model.MatrizInput == null || model.VectorBInput == null || model.AproximacionInicialInput == null)
            {
                model.Mensaje = "Error: Debes ingresar todos los datos requeridos.";
                model.TipoMensaje = "error";
                return View(model);
            }

            int dimension = model.MatrizInput.Count;
            model.Dimension = dimension;
            model.MatrizCoeficientes = new double[dimension, dimension];
            model.VectorIndependiente = new double[dimension];
            model.AproximacionInicial = new double[dimension];

            for (int i = 0; i < dimension; i++)
            {
                for (int j = 0; j < dimension; j++)
                {
                    model.MatrizCoeficientes[i, j] = model.MatrizInput[i][j];
                }
                model.VectorIndependiente[i] = model.VectorBInput[i];
                model.AproximacionInicial[i] = model.AproximacionInicialInput[i];
            }

            model.Iteraciones = new List<IteracionGaussSeidel>();
            double[] x = (double[])model.AproximacionInicial.Clone();
            double[] xPrev = new double[dimension];
            double tol = model.Tolerancia;
            int maxIter = model.MaxIteraciones;
            bool convergencia = false;

            int usuarioId = Session["UsuarioId"] != null ? Convert.ToInt32(Session["UsuarioId"]) : 0;
            if (usuarioId == 0)
            {
                model.Mensaje = "Error: No se ha iniciado sesión.";
                model.TipoMensaje = "error";
                return View(model);
            }

            int grupoId;

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"
        INSERT INTO GaussSeidelGrupo 
        (Dimension, MaxIteraciones, Tolerancia, Fecha, UsuarioId) 
        VALUES 
        (@Dimension, @MaxIteraciones, @Tolerancia, @Fecha, @UsuarioId); 
        SELECT last_insert_rowid();";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@Dimension", dimension);
                    cmdGrupo.Parameters.AddWithValue("@MaxIteraciones", maxIter);
                    cmdGrupo.Parameters.AddWithValue("@Tolerancia", tol.ToString("0.############################"));
                    cmdGrupo.Parameters.AddWithValue("@Fecha", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);
                    grupoId = Convert.ToInt32(cmdGrupo.ExecuteScalar());
                }

                string queryResultados = @"
        INSERT INTO GaussSeidelResultados 
        (GrupoId, Iteracion, Valores, Error, UsuarioId, Timestamp) 
        VALUES 
        (@GrupoId, @Iteracion, @Valores, @Error, @UsuarioId, @Timestamp)";

                for (int iter = 0; iter < maxIter; iter++)
                {
                    Array.Copy(x, xPrev, dimension);
                    for (int i = 0; i < dimension; i++)
                    {
                        double suma = 0;
                        for (int j = 0; j < dimension; j++)
                        {
                            if (j != i)
                                suma += model.MatrizCoeficientes[i, j] * x[j];
                        }
                        x[i] = (model.VectorIndependiente[i] - suma) / model.MatrizCoeficientes[i, i];
                    }

                    double error = 0;
                    for (int i = 0; i < dimension; i++)
                    {
                        error = Math.Max(error, Math.Abs(x[i] - xPrev[i]));
                    }

                    model.Iteraciones.Add(new IteracionGaussSeidel
                    {
                        Iteracion = iter + 1,
                        GrupoId = grupoId,
                        Valores = (double[])x.Clone(),
                        Error = error
                    });

                    using (SQLiteCommand cmdRes = new SQLiteCommand(queryResultados, conn))
                    {
                        cmdRes.Parameters.AddWithValue("@GrupoId", grupoId);
                        cmdRes.Parameters.AddWithValue("@Iteracion", iter + 1);
                        cmdRes.Parameters.AddWithValue("@Valores", string.Join(",", x.Select(v => v.ToString("G17"))));
                        cmdRes.Parameters.AddWithValue("@Error", error);
                        cmdRes.Parameters.AddWithValue("@UsuarioId", usuarioId);
                        cmdRes.Parameters.AddWithValue("@Timestamp", DateTime.Now);
                        cmdRes.ExecuteNonQuery();
                    }

                    if (error < tol)
                    {
                        convergencia = true;
                        break;
                    }
                }

                model.Solucion = x;
                model.Mensaje = convergencia
                    ? "Éxito: El método convergió a una solución dentro de la tolerancia."
                    : "Advertencia: Se alcanzó el número máximo de iteraciones sin convergencia.";
                model.TipoMensaje = convergencia ? "success" : "warning";

                return View("Index", model);
            }
        }

        public ActionResult Historial()
        {
            int usuarioId = Session["UsuarioId"] != null ? Convert.ToInt32(Session["UsuarioId"]) : 0;

            if (usuarioId == 0)
            {
                return Content("Error: No se ha iniciado sesión.");
            }

            var grupos = new List<GaussSeidelModel>();

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"
            SELECT Id, Dimension, MaxIteraciones, Tolerancia, Fecha, UsuarioId 
            FROM GaussSeidelGrupo 
            WHERE UsuarioId = @UsuarioId 
            ORDER BY Fecha DESC";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);

                    using (SQLiteDataReader readerGrupo = cmdGrupo.ExecuteReader())
                    {
                        while (readerGrupo.Read())
                        {
                            int grupoId = readerGrupo.GetInt32(0);
                            int dimension = readerGrupo.GetInt32(1);
                            int maxIter = readerGrupo.GetInt32(2);
                            string toleranciaStr = readerGrupo.GetString(3);
                            double tolerancia = double.TryParse(toleranciaStr, out double tol) ? tol : 0.0;
                            string fechaStr = readerGrupo.IsDBNull(4) ? "" : readerGrupo.GetString(4);
                            DateTime fecha = DateTime.TryParse(fechaStr, out DateTime f) ? f : DateTime.MinValue;
                            int userId = readerGrupo.GetInt32(5);

                            var grupo = new GaussSeidelModel
                            {
                                GrupoId = grupoId,
                                Dimension = dimension,
                                MaxIteraciones = maxIter,
                                Tolerancia = tolerancia,
                                Fecha = fecha,
                                UsuarioId = userId,
                                Solucion = new double[dimension],
                                Iteraciones = new List<IteracionGaussSeidel>()
                            };

                            // Cargar iteraciones
                            string queryIteraciones = @"
                        SELECT Iteracion, Valores, Error 
                        FROM GaussSeidelResultados 
                        WHERE GrupoId = @GrupoId 
                        ORDER BY Iteracion ASC";

                            using (SQLiteCommand cmdIteraciones = new SQLiteCommand(queryIteraciones, conn))
                            {
                                cmdIteraciones.Parameters.AddWithValue("@GrupoId", grupoId);

                                using (SQLiteDataReader readerIteraciones = cmdIteraciones.ExecuteReader())
                                {
                                    while (readerIteraciones.Read())
                                    {
                                        int iteracion = readerIteraciones.GetInt32(0);
                                        string valoresStr = readerIteraciones.GetString(1);
                                        double[] valores = valoresStr
                                            .Split(',')
                                            .Select(v => double.TryParse(v, out double result) ? result : 0.0)
                                            .ToArray();

                                        double error = readerIteraciones.GetDouble(2);

                                        grupo.Iteraciones.Add(new IteracionGaussSeidel
                                        {
                                            Iteracion = iteracion,
                                            Valores = valores,
                                            Error = error,
                                            GrupoId = grupoId
                                        });

                                        grupo.Solucion = valores;
                                    }
                                }
                            }

                            grupos.Add(grupo);
                        }
                    }
                }
            }

            return View(grupos);
        }


        public ActionResult GenerarPDF(int grupoId)
        {
            if (grupoId <= 0)
                return Content("ID de grupo no válido.");

            GaussSeidelModel grupo = null;

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"
        SELECT Id, Dimension, Tolerancia, MaxIteraciones, Fecha 
        FROM GaussSeidelGrupo 
        WHERE Id = @GrupoId";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@GrupoId", grupoId);
                    using (SQLiteDataReader reader = cmdGrupo.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string toleranciaStr = reader.GetString(2);
                            double tolerancia = double.TryParse(toleranciaStr, out double tol) ? tol : 0.0;

                            string fechaStr = reader.IsDBNull(4) ? "" : reader.GetString(4);
                            DateTime fecha = DateTime.TryParse(fechaStr, out DateTime f) ? f : DateTime.MinValue;

                            grupo = new GaussSeidelModel
                            {
                                GrupoId = reader.GetInt32(0),
                                Dimension = reader.GetInt32(1),
                                Tolerancia = tolerancia,
                                MaxIteraciones = reader.GetInt32(3),
                                Fecha = fecha,
                                Iteraciones = new List<IteracionGaussSeidel>()
                            };
                        }
                    }
                }

                if (grupo == null)
                    return Content("No se encontró el grupo para generar el PDF.");

                string queryIteraciones = @"
        SELECT Iteracion, Valores, Error 
        FROM GaussSeidelResultados 
        WHERE GrupoId = @GrupoId 
        ORDER BY Iteracion ASC";

                using (SQLiteCommand cmdIteraciones = new SQLiteCommand(queryIteraciones, conn))
                {
                    cmdIteraciones.Parameters.AddWithValue("@GrupoId", grupo.GrupoId);
                    using (SQLiteDataReader reader = cmdIteraciones.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string valoresTexto = reader.GetString(1);
                            double[] valores = valoresTexto
                                .Split(',')
                                .Select(v => double.TryParse(v, out double result) ? result : 0.0)
                                .ToArray();

                            grupo.Iteraciones.Add(new IteracionGaussSeidel
                            {
                                Iteracion = reader.GetInt32(0),
                                Valores = valores,
                                Error = reader.GetDouble(2)
                            });
                        }
                    }
                }
            }

            if (!grupo.Iteraciones.Any())
                return Content("No se encontraron iteraciones para el grupo.");

            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4, 40, 40, 30, 40);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                string imagePath = Server.MapPath("~/Content/Fotos/Umg.png");
                Image logo = Image.GetInstance(imagePath);
                logo.ScaleAbsolute(60f, 60f);

                // Tabla de encabezado con logo y título en paralelo
                PdfPTable headerTable = new PdfPTable(2);
                headerTable.WidthPercentage = 100;
                headerTable.SetWidths(new float[] { 1.3f, 3.7f }); // Ajuste para mover el título a la izquierda

                PdfPCell logoCell = new PdfPCell(logo);
                logoCell.Border = Rectangle.NO_BORDER;
                logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
                logoCell.VerticalAlignment = Element.ALIGN_MIDDLE;
                headerTable.AddCell(logoCell);

                PdfPCell titleCell = new PdfPCell();
                titleCell.Border = Rectangle.NO_BORDER;
                titleCell.VerticalAlignment = Element.ALIGN_MIDDLE;
                titleCell.PaddingLeft = -100f;

                Paragraph titulo = new Paragraph("Reporte de Método de Gauss-Seidel", new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD));
                titulo.Alignment = Element.ALIGN_CENTER;

                titleCell.AddElement(titulo);
                headerTable.AddCell(titleCell);

                doc.Add(headerTable);
                doc.Add(new Paragraph("\n"));

                doc.Add(new Paragraph($"Dimensión del sistema: {grupo.Dimension}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Máx. Iteraciones: {grupo.MaxIteraciones}, Tolerancia: {grupo.Tolerancia.ToString("0.#####################")}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Fecha: {grupo.Fecha:yyyy-MM-dd HH:mm:ss}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph("\n"));

                PdfPTable table = new PdfPTable(grupo.Dimension + 2);
                table.WidthPercentage = 100;

                // Encabezados
                PdfPCell cell;
                cell = new PdfPCell(new Phrase("Iteración", FontFactory.GetFont("Arial", 11, Font.BOLD)));
                cell.BackgroundColor = new BaseColor(230, 230, 250);
                cell.HorizontalAlignment = Element.ALIGN_CENTER;
                table.AddCell(cell);

                for (int i = 0; i < grupo.Dimension; i++)
                {
                    cell = new PdfPCell(new Phrase($"x{i + 1}", FontFactory.GetFont("Arial", 11, Font.BOLD)));
                    cell.BackgroundColor = new BaseColor(230, 230, 250);
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    table.AddCell(cell);
                }

                cell = new PdfPCell(new Phrase("Error", FontFactory.GetFont("Arial", 11, Font.BOLD)));
                cell.BackgroundColor = new BaseColor(230, 230, 250);
                cell.HorizontalAlignment = Element.ALIGN_CENTER;
                table.AddCell(cell);

                // Iteraciones
                foreach (var iter in grupo.Iteraciones)
                {
                    table.AddCell(iter.Iteracion.ToString());
                    foreach (var valor in iter.Valores)
                        table.AddCell(valor.ToString("F6"));

                    table.AddCell(iter.Error.ToString("F9"));
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"GaussSeidel_Grupo_{grupo.GrupoId}.pdf");
            }
        }


        public ActionResult BorrarGrupo(int grupoId)
        {
            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryDeleteResultados = "DELETE FROM GaussSeidelResultados WHERE GrupoId = @GrupoId";
                string queryDeleteGrupo = "DELETE FROM GaussSeidelGrupo WHERE Id = @GrupoId";

                using (SQLiteCommand cmdResultados = new SQLiteCommand(queryDeleteResultados, conn))
                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryDeleteGrupo, conn))
                {
                    cmdResultados.Parameters.AddWithValue("@GrupoId", grupoId);
                    cmdGrupo.Parameters.AddWithValue("@GrupoId", grupoId);

                    cmdResultados.ExecuteNonQuery();
                    cmdGrupo.ExecuteNonQuery();
                }
            }

            return RedirectToAction("Historial");
        }


        private int ObtenerUsuarioActual()
        {
            return Session["UsuarioId"] != null ? Convert.ToInt32(Session["UsuarioId"]) : 0;
        }

        private List<IteracionGaussSeidel> EjecutarGaussSeidel(
            double[,] A,
            double[] b,
            double[] x0,
            double tolerancia,
            int maxIteraciones,
            out double[] solucionFinal)
        {
            int n = b.Length;
            double[] x = (double[])x0.Clone();
            double[] xAnterior = new double[n];
            var iteraciones = new List<IteracionGaussSeidel>();

            for (int iter = 0; iter < maxIteraciones; iter++)
            {
                Array.Copy(x, xAnterior, n);

                for (int i = 0; i < n; i++)
                {
                    double suma = 0;
                    for (int j = 0; j < n; j++)
                    {
                        if (j != i)
                            suma += A[i, j] * x[j];
                    }

                    if (A[i, i] == 0)
                        throw new Exception($"División por cero detectada en A[{i},{i}].");

                    x[i] = (b[i] - suma) / A[i, i];
                }

                double error = 0;
                for (int i = 0; i < n; i++)
                    error = Math.Max(error, Math.Abs(x[i] - xAnterior[i]));

                iteraciones.Add(new IteracionGaussSeidel
                {
                    Iteracion = iter + 1,
                    Valores = (double[])x.Clone(),
                    Error = error
                });

                if (error < tolerancia)
                    break;
            }

            solucionFinal = x;
            return iteraciones;
        }
    }
}