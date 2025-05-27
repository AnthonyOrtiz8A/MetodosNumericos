using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Web.Mvc;
using Calculadora_de_Ecuaciones.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using NCalc;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Internal;

namespace Calculadora_de_Ecuaciones.Controllers
{
    public class NewtonRaphsonController : Controller
    {
        private string GetConnectionString()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "CalculadoraBD.s3db");
            return $"Data Source={dbPath};Version=3;";
        }
        public ActionResult Index()
        {
            var model = new NewtonRaphsonModel
            {
                Mensaje = string.Empty, // ✅ Evita mostrar errores al cargar la página por primera vez
                Iteraciones = new List<IteracionNewton>()
            };
            return View(model);
        }

        [HttpPost]
        public ActionResult Index(NewtonRaphsonModel model)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Funcion))
            {
                model.Mensaje = "Error: Debes ingresar una función.";
                model.TipoMensaje = "error";
                return View(model);
            }

            model.Iteraciones = new List<IteracionNewton>();
            double x = model.X0;
            int maxIter = model.MaxIter;
            double tol = model.Tolerancia;
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
            INSERT INTO NewtonRaphsonGrupo 
            (Funcion, X0, MaxIter, Tolerancia, Fecha, UsuarioId) 
            VALUES 
            (@Funcion, @X0, @MaxIter, @Tolerancia, @Fecha, @UsuarioId); 
            SELECT last_insert_rowid();";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@Funcion", model.Funcion);
                    cmdGrupo.Parameters.AddWithValue("@X0", model.X0);
                    cmdGrupo.Parameters.AddWithValue("@MaxIter", model.MaxIter);
                    cmdGrupo.Parameters.AddWithValue("@Tolerancia", tol.ToString("0.############################")); // ✅ Guardar como decimal
                    cmdGrupo.Parameters.AddWithValue("@Fecha", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);
                    grupoId = Convert.ToInt32(cmdGrupo.ExecuteScalar());
                }

                string queryResultados = @"
            INSERT INTO NewtonRaphsonResultados 
            (GrupoId, Iteracion, X, FX, DFX, NextX, MargenError, UsuarioId, Timestamp) 
            VALUES 
            (@GrupoId, @Iteracion, @X, @FX, @DFX, @NextX, @MargenError, @UsuarioId, @Timestamp)";

                for (int i = 0; i < maxIter; i++)
                {
                    double fx = EvaluarFuncion(model.Funcion, x);
                    double dfx = Derivada(model.Funcion, x);

                    if (Math.Abs(dfx) < 1e-10)
                    {
                        model.Mensaje = "Error: La derivada es cercana a cero. Método detenido.";
                        model.TipoMensaje = "error";
                        return View(model);
                    }

                    double nextX = x - fx / dfx;
                    double margenError = Math.Abs(nextX - x);

                    model.Iteraciones.Add(new IteracionNewton
                    {
                        Iteracion = i + 1,
                        GrupoId = grupoId,
                        X = x,
                        FX = fx,
                        DFX = dfx,
                        NextX = nextX,
                        MargenError = margenError
                    });

                    using (SQLiteCommand cmdRes = new SQLiteCommand(queryResultados, conn))
                    {
                        cmdRes.Parameters.AddWithValue("@GrupoId", grupoId);
                        cmdRes.Parameters.AddWithValue("@Iteracion", i + 1);
                        cmdRes.Parameters.AddWithValue("@X", x);
                        cmdRes.Parameters.AddWithValue("@FX", fx);
                        cmdRes.Parameters.AddWithValue("@DFX", dfx);
                        cmdRes.Parameters.AddWithValue("@NextX", nextX);
                        cmdRes.Parameters.AddWithValue("@MargenError", margenError);
                        cmdRes.Parameters.AddWithValue("@UsuarioId", usuarioId);
                        cmdRes.Parameters.AddWithValue("@Timestamp", DateTime.Now);
                        cmdRes.ExecuteNonQuery();
                    }

                    if (margenError < tol)
                    {
                        convergencia = true;
                        break;
                    }

                    x = nextX;
                }

                if (!convergencia)
                {
                    model.Mensaje = "Advertencia: Se alcanzó el número máximo de iteraciones sin encontrar una raíz dentro de la tolerancia.";
                    model.TipoMensaje = "warning";
                }

                return View(model);
            }
        }


        public ActionResult Historial()
        {
            int usuarioId = Session["UsuarioId"] != null ? Convert.ToInt32(Session["UsuarioId"]) : 0;

            if (usuarioId == 0)
            {
                return Content("Error: No se ha iniciado sesión.");
            }

            var grupos = new List<NewtonRaphsonModel>();

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();
                string queryGrupo = "SELECT * FROM NewtonRaphsonGrupo WHERE UsuarioId = @UsuarioId ORDER BY Fecha DESC";
                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);
                    using (SQLiteDataReader readerGrupo = cmdGrupo.ExecuteReader())
                    {
                        while (readerGrupo.Read())
                        {
                            var grupo = new NewtonRaphsonModel
                            {
                                GrupoId = readerGrupo.GetInt32(0),
                                Funcion = readerGrupo.GetString(1),
                                X0 = readerGrupo.GetDouble(2),
                                MaxIter = readerGrupo.GetInt32(3),
                                Tolerancia = readerGrupo.GetDouble(4),
                                Fecha = readerGrupo.GetDateTime(5),
                                UsuarioId = readerGrupo.GetInt32(6),
                                Iteraciones = new List<IteracionNewton>() // ✅ Inicializar lista vacía
                            };

                            // ✅ Cargar iteraciones de este grupo desde NewtonRaphsonResultados incluyendo el margen de error
                            string queryIteraciones = "SELECT Iteracion, X, FX, DFX, NextX, MargenError FROM NewtonRaphsonResultados WHERE GrupoId = @GrupoId ORDER BY Iteracion ASC";
                            using (SQLiteCommand cmdIteraciones = new SQLiteCommand(queryIteraciones, conn))
                            {
                                cmdIteraciones.Parameters.AddWithValue("@GrupoId", grupo.GrupoId);
                                using (SQLiteDataReader readerIteraciones = cmdIteraciones.ExecuteReader())
                                {
                                    while (readerIteraciones.Read())
                                    {
                                        grupo.Iteraciones.Add(new IteracionNewton
                                        {
                                            Iteracion = readerIteraciones.GetInt32(0),
                                            X = readerIteraciones.GetDouble(1),
                                            FX = readerIteraciones.GetDouble(2),
                                            DFX = readerIteraciones.GetDouble(3),
                                            NextX = readerIteraciones.GetDouble(4),
                                            MargenError = readerIteraciones.IsDBNull(5) ? 0.0 : readerIteraciones.GetDouble(5), // ✅ Evita error si MargenError es NULL
                                            GrupoId = grupo.GrupoId
                                        });
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
        public ActionResult BorrarGrupo(int grupoId)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
                {
                    conn.Open();

                    // ✅ Eliminar primero las iteraciones del grupo
                    string queryEliminarResultados = "DELETE FROM NewtonRaphsonResultados WHERE GrupoId = @GrupoId";
                    using (SQLiteCommand cmdResultados = new SQLiteCommand(queryEliminarResultados, conn))
                    {
                        cmdResultados.Parameters.AddWithValue("@GrupoId", grupoId);
                        cmdResultados.ExecuteNonQuery();
                    }

                    // ✅ Luego eliminar el grupo de cálculos
                    string queryEliminarGrupo = "DELETE FROM NewtonRaphsonGrupo WHERE GrupoId = @GrupoId";
                    using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryEliminarGrupo, conn))
                    {
                        cmdGrupo.Parameters.AddWithValue("@GrupoId", grupoId);
                        cmdGrupo.ExecuteNonQuery();
                    }
                }

                TempData["Mensaje"] = "Grupo eliminado correctamente.";
                return RedirectToAction("Historial");
            }
            catch (Exception ex)
            {
                TempData["Mensaje"] = $"Error al eliminar el grupo: {ex.Message}";
                return RedirectToAction("Historial");
            }
        }

        public ActionResult GenerarPDF(int grupoId)
        {
            NewtonRaphsonModel grupo = null;

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"
            SELECT GrupoId, Funcion, X0, MaxIter, Tolerancia, Fecha 
            FROM NewtonRaphsonGrupo 
            WHERE GrupoId = @GrupoId";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@GrupoId", grupoId);
                    using (SQLiteDataReader reader = cmdGrupo.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            grupo = new NewtonRaphsonModel
                            {
                                GrupoId = reader.GetInt32(0),
                                Funcion = reader.GetString(1),
                                X0 = reader.GetDouble(2),
                                MaxIter = reader.GetInt32(3),
                                Tolerancia = reader.GetDouble(4),
                                Fecha = reader.GetDateTime(5),
                                Iteraciones = new List<IteracionNewton>()
                            };
                        }
                    }
                }

                if (grupo == null)
                    return Content("No hay operaciones recientes para generar el PDF.");

                string queryResultados = @"
            SELECT Iteracion, X, FX, DFX, NextX, MargenError 
            FROM NewtonRaphsonResultados 
            WHERE GrupoId = @GrupoId 
            ORDER BY Iteracion ASC";

                using (SQLiteCommand cmdResultados = new SQLiteCommand(queryResultados, conn))
                {
                    cmdResultados.Parameters.AddWithValue("@GrupoId", grupo.GrupoId);
                    using (SQLiteDataReader reader = cmdResultados.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            grupo.Iteraciones.Add(new IteracionNewton
                            {
                                Iteracion = reader.GetInt32(0),
                                X = reader.GetDouble(1),
                                FX = reader.GetDouble(2),
                                DFX = reader.GetDouble(3),
                                NextX = reader.GetDouble(4),
                                MargenError = reader.GetDouble(5),
                                GrupoId = grupo.GrupoId
                            });
                        }
                    }
                }
            }

            if (grupo.Iteraciones == null || !grupo.Iteraciones.Any())
                return Content("No se encontraron iteraciones para el último cálculo.");

            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4, 40, 40, 80, 40);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                string imagePath = Server.MapPath("~/Content/Fotos/Umg.png");
                Image logo = Image.GetInstance(imagePath);
                logo.ScaleAbsolute(60f, 60f);
                logo.SetAbsolutePosition(doc.LeftMargin, doc.PageSize.Height - 70);
                doc.Add(logo);

                Paragraph titulo = new Paragraph("Reporte de Método de Newton-Raphson", new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD));
                titulo.Alignment = Element.ALIGN_CENTER;
                titulo.SpacingAfter = 20f;
                doc.Add(titulo);

                doc.Add(new Paragraph($"Función: {grupo.Funcion}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Valor inicial: X₀={grupo.X0}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Máx. Iteraciones: {grupo.MaxIter}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Tolerancia: {grupo.Tolerancia.ToString("0.############################")}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Fecha: {grupo.Fecha}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph("\n"));

                PdfPTable table = new PdfPTable(6);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 1.5f, 2f, 2f, 2f, 2f, 2f });

                string[] headers = { "Iteración", "X", "f(X)", "f'(X)", "Siguiente X", "Margen de Error" };
                foreach (var header in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(header, FontFactory.GetFont("Arial", 11, Font.BOLD)));
                    cell.BackgroundColor = new BaseColor(230, 230, 250);
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    table.AddCell(cell);
                }

                foreach (var resultado in grupo.Iteraciones)
                {
                    table.AddCell(new PdfPCell(new Phrase(resultado.Iteracion.ToString())));
                    table.AddCell(new PdfPCell(new Phrase(resultado.X.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.FX.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.DFX.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.NextX.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.MargenError.ToString("F6"))));
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"Ultima_Operacion_Newton_{grupo.GrupoId}.pdf");
            }
        }

        private double EvaluarFuncion(string funcion, double x)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(funcion))
                    throw new Exception("La función no puede estar vacía.");

                // ✅ Convertir expresiones matemáticas comunes a sintaxis de NCalc
                string funcionProcesada = ProcesarFuncion(funcion);

                Expression e = new Expression(funcionProcesada, EvaluateOptions.IgnoreCase);
                e.Parameters["x"] = x;

                e.EvaluateFunction += (name, args) =>
                {
                    switch (name.ToLower())
                    {
                        case "exp": args.Result = Math.Exp(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                        case "pow": args.Result = Math.Pow(Convert.ToDouble(args.Parameters[0].Evaluate()), Convert.ToDouble(args.Parameters[1].Evaluate())); break;
                        case "ln": args.Result = Math.Log(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                        case "log": args.Result = Math.Log10(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                        case "sqrt": args.Result = Math.Sqrt(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                        case "sin": args.Result = Math.Sin(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                        case "cos": args.Result = Math.Cos(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                        case "tan": args.Result = Math.Tan(Convert.ToDouble(args.Parameters[0].Evaluate())); break;
                    }
                };

                return Convert.ToDouble(e.Evaluate());
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al evaluar la función '{funcion}' con x={x}: {ex.Message}");
            }

        }
        private string ProcesarFuncion(string expr)
        {
            expr = expr.ToLower();

            // ✅ Convertir "e^x" a "Exp(x)"
            expr = Regex.Replace(expr, @"e\^(\(?-?[\d\.x\+\-\*/\^\(\)]+?\)?)", "exp($1)");

            // ✅ Reemplazar "e" por Math.E cuando no está en una potencia
            expr = Regex.Replace(expr, @"(?<!\^)e\b", "Math.E");

            // ✅ Convertir "x^n" a "Pow(x,n)" para exponentes numéricos
            expr = Regex.Replace(expr, @"([\w\)\.]+)\s*\^\s*([\w\(\)\.\-]+)", "Pow($1, $2)");

            return expr;
        }
        private double Derivada(string funcion, double x, double h = 1e-5)
        {
            try
            {
                double fxh1 = EvaluarFuncion(funcion, x + h);
                double fxh2 = EvaluarFuncion(funcion, x - h);
                return (fxh1 - fxh2) / (2 * h);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al calcular la derivada: {ex.Message}");
            }
        }
    }
}