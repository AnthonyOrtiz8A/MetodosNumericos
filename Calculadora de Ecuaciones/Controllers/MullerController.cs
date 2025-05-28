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
using System.Linq;

namespace Calculadora_de_Ecuaciones.Controllers
{
    public class MullerController : Controller
    {

        private string GetConnectionString()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "CalculadoraBD.s3db");
            return $"Data Source={dbPath};Version=3;";
        }

        public ActionResult Index()
        {
            return View(new MullerModel());
        }

        [HttpPost]
        public ActionResult Calculate(MullerModel model)
        {
            if (string.IsNullOrEmpty(model.Funcion))
            {
                model.Mensaje = "Debes ingresar una función válida.";
                model.TipoMensaje = "error";
                return View("Index", model);
            }

            try
            {
                Func<double, double> function = x => EvaluarFuncion(model.Funcion, x);

                double testValue;
                try
                {
                    testValue = function(model.X0);
                }
                catch
                {
                    model.Mensaje = "Error: No se pudo evaluar la función. Verifica su formato.";
                    model.TipoMensaje = "error";
                    model.Iteraciones = null;
                    return View("Index", model);
                }

                if (Math.Abs(testValue) < 1e-10)
                {
                    model.Mensaje = "Error: Función no válida (muy pequeña).";
                    model.TipoMensaje = "error";
                    model.Iteraciones = null;
                    return View("Index", model);
                }

                model.Iteraciones = EjecutarMuller(model.X0, model.X1, model.X2, model.Tolerancia, model.MaxIter, function);

                int usuarioId = Convert.ToInt32(Session["UsuarioId"]);
                using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
                {
                    conn.Open();

                    string queryGrupo = @"
            INSERT INTO MullerGrupo 
            (Funcion, X0, X1, X2, MaxIter, Tolerancia, UsuarioId, Fecha) 
            VALUES 
            (@Funcion, @X0, @X1, @X2, @MaxIter, @Tolerancia, @UsuarioId, @Fecha); 
            SELECT last_insert_rowid();";

                    int grupoId;

                    using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                    {
                        cmdGrupo.Parameters.AddWithValue("@Funcion", model.Funcion);
                        cmdGrupo.Parameters.AddWithValue("@X0", model.X0);
                        cmdGrupo.Parameters.AddWithValue("@X1", model.X1);
                        cmdGrupo.Parameters.AddWithValue("@X2", model.X2);
                        cmdGrupo.Parameters.AddWithValue("@MaxIter", model.MaxIter);
                        cmdGrupo.Parameters.AddWithValue("@Tolerancia", model.Tolerancia.ToString("0.############################")); 
                        cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);
                        cmdGrupo.Parameters.AddWithValue("@Fecha", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        grupoId = Convert.ToInt32(cmdGrupo.ExecuteScalar());
                    }

                    model.GrupoId = grupoId;

                    string queryResultados = @"
            INSERT INTO MullerResultados 
            (GrupoId, Iteracion, X0, X1, X2, A, B, C, NextX, MargenError) 
            VALUES 
            (@GrupoId, @Iteracion, @X0, @X1, @X2, @A, @B, @C, @NextX, @MargenError)";

                    foreach (var iteracion in model.Iteraciones)
                    {
                        using (SQLiteCommand cmdRes = new SQLiteCommand(queryResultados, conn))
                        {
                            cmdRes.Parameters.AddWithValue("@GrupoId", grupoId);
                            cmdRes.Parameters.AddWithValue("@Iteracion", iteracion.Iteracion);
                            cmdRes.Parameters.AddWithValue("@X0", iteracion.X0);
                            cmdRes.Parameters.AddWithValue("@X1", iteracion.X1);
                            cmdRes.Parameters.AddWithValue("@X2", iteracion.X2);
                            cmdRes.Parameters.AddWithValue("@A", iteracion.A);
                            cmdRes.Parameters.AddWithValue("@B", iteracion.B);
                            cmdRes.Parameters.AddWithValue("@C", iteracion.C);
                            cmdRes.Parameters.AddWithValue("@NextX", iteracion.NextX);
                            cmdRes.Parameters.AddWithValue("@MargenError", iteracion.MargenError);
                            cmdRes.ExecuteNonQuery();
                        }
                    }
                }

                model.Root = model.Iteraciones.Last().NextX;
                double ultimoMargenError = model.Iteraciones.Last().MargenError;

                if (ultimoMargenError > model.Tolerancia)
                {
                    model.Mensaje = "Advertencia: No se encontró la raíz.";
                    model.TipoMensaje = "warning";
                }
            }
            catch (Exception ex)
            {
                model.Mensaje = $"Error al calcular la raíz: {ex.Message}";
                model.TipoMensaje = "error";
                model.Iteraciones = null;
                model.Root = null;
            }

            return View("Index", model);
        }



        public ActionResult Historial()
        {
            int usuarioId = Convert.ToInt32(Session["UsuarioId"]);
            List<MullerModel> historial = new List<MullerModel>();

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();
                string queryGrupo = "SELECT * FROM MullerGrupo WHERE UsuarioId = @UsuarioId ORDER BY Fecha DESC";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);

                    using (SQLiteDataReader readerGrupo = cmdGrupo.ExecuteReader())
                    {
                        while (readerGrupo.Read())
                        {
                            MullerModel grupo = new MullerModel
                            {
                                GrupoId = readerGrupo.GetInt32(0),
                                Funcion = readerGrupo.GetString(1),
                                X0 = readerGrupo.GetDouble(2),
                                X1 = readerGrupo.GetDouble(3),
                                X2 = readerGrupo.GetDouble(4),
                                MaxIter = readerGrupo.GetInt32(5),
                                Tolerancia = readerGrupo.GetDouble(6),
                                Fecha = DateTime.Parse(readerGrupo.GetString(7)),
                                UsuarioId = readerGrupo.GetInt32(8),
                                Iteraciones = new List<IteracionMuller>()
                            };

                            string queryIteraciones = "SELECT GrupoId, Iteracion, X0, X1, X2, A, B, C, NextX, MargenError FROM MullerResultados WHERE GrupoId = @GrupoId ORDER BY Iteracion";

                            using (SQLiteCommand cmdIteraciones = new SQLiteCommand(queryIteraciones, conn))
                            {
                                cmdIteraciones.Parameters.AddWithValue("@GrupoId", grupo.GrupoId);

                                using (SQLiteDataReader readerIteraciones = cmdIteraciones.ExecuteReader())
                                {
                                    while (readerIteraciones.Read())
                                    {
                                        grupo.Iteraciones.Add(new IteracionMuller
                                        {
                                            GrupoId = readerIteraciones.GetInt32(0),
                                            Iteracion = readerIteraciones.GetInt32(1),
                                            X0 = readerIteraciones.GetDouble(2),
                                            X1 = readerIteraciones.GetDouble(3),
                                            X2 = readerIteraciones.GetDouble(4),
                                            A = readerIteraciones.GetDouble(5),
                                            B = readerIteraciones.GetDouble(6),
                                            C = readerIteraciones.GetDouble(7),
                                            NextX = readerIteraciones.GetDouble(8),
                                            MargenError = readerIteraciones.GetDouble(9)
                                        });
                                    }
                                }
                            }

                            historial.Add(grupo);
                        }
                    }
                }
            }

            return View(historial);
        }

        public ActionResult GenerarPDF(int grupoId)
        {
            if (grupoId <= 0)
                return Content("ID de grupo no válido.");

            MullerModel grupo = null;
            List<IteracionMuller> iteraciones = new List<IteracionMuller>();

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"
        SELECT GrupoId, Funcion, X0, X1, X2, MaxIter, Tolerancia, Fecha 
        FROM MullerGrupo 
        WHERE GrupoId = @GrupoId";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@GrupoId", grupoId);
                    using (SQLiteDataReader reader = cmdGrupo.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            grupo = new MullerModel
                            {
                                GrupoId = reader.GetInt32(0),
                                Funcion = reader.GetString(1),
                                X0 = reader.GetDouble(2),
                                X1 = reader.GetDouble(3),
                                X2 = reader.GetDouble(4),
                                MaxIter = reader.GetInt32(5),
                                Tolerancia = reader.GetDouble(6),
                                Fecha = DateTime.Parse(reader.GetString(7))
                            };
                        }
                    }
                }

                if (grupo == null)
                    return Content("No hay operaciones recientes para generar el PDF.");

                string queryIteraciones = @"
        SELECT Iteracion, X0, X1, X2, A, B, C, NextX, MargenError 
        FROM MullerResultados 
        WHERE GrupoId = @GrupoId 
        ORDER BY Iteracion ASC";

                using (SQLiteCommand cmdIteraciones = new SQLiteCommand(queryIteraciones, conn))
                {
                    cmdIteraciones.Parameters.AddWithValue("@GrupoId", grupo.GrupoId);
                    using (SQLiteDataReader reader = cmdIteraciones.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            iteraciones.Add(new IteracionMuller
                            {
                                Iteracion = reader.GetInt32(0),
                                X0 = reader.GetDouble(1),
                                X1 = reader.GetDouble(2),
                                X2 = reader.GetDouble(3),
                                A = reader.GetDouble(4),
                                B = reader.GetDouble(5),
                                C = reader.GetDouble(6),
                                NextX = reader.GetDouble(7),
                                MargenError = reader.GetDouble(8)
                            });
                        }
                    }
                }
            }

            if (!iteraciones.Any())
                return Content("No se encontraron iteraciones para el último cálculo.");

            using (MemoryStream ms = new MemoryStream())
            {
                Document doc = new Document(PageSize.A4.Rotate(), 40, 40, 40, 40);
                PdfWriter writer = PdfWriter.GetInstance(doc, ms);

                doc.Open();

                string imagePath = Server.MapPath("~/Content/Fotos/Umg.png");
                Image logo = Image.GetInstance(imagePath);
                logo.ScaleAbsolute(60f, 60f);

                PdfPTable headerTable = new PdfPTable(2);
                headerTable.WidthPercentage = 100;
                headerTable.SetWidths(new float[] { 1.5f, 3.5f });

                PdfPCell logoCell = new PdfPCell(logo);
                logoCell.Border = Rectangle.NO_BORDER;
                logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
                logoCell.VerticalAlignment = Element.ALIGN_MIDDLE;
                headerTable.AddCell(logoCell);

                PdfPCell titleCell = new PdfPCell();
                titleCell.Border = Rectangle.NO_BORDER;
                titleCell.HorizontalAlignment = Element.ALIGN_LEFT; 
                titleCell.VerticalAlignment = Element.ALIGN_MIDDLE;
                titleCell.PaddingLeft = 30f; 

                Paragraph title = new Paragraph("Reporte de Método de Müller", new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD));
                titleCell.AddElement(title);
                headerTable.AddCell(titleCell);

                doc.Add(headerTable);
                doc.Add(new Paragraph("\n"));

                doc.Add(new Paragraph($"Función: {grupo.Funcion}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Valores iniciales: x0 = {grupo.X0}, x1 = {grupo.X1}, x2 = {grupo.X2}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Máx. Iteraciones: {grupo.MaxIter}, Tolerancia: {grupo.Tolerancia.ToString("0.############################")}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Fecha: {grupo.Fecha}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph("\n"));

                PdfPTable table = new PdfPTable(9);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 1.2f, 1.5f, 1.5f, 1.5f, 1.5f, 1.5f, 1.5f, 1.8f, 1.8f });

                string[] headers = { "Iteración", "X0", "X1", "X2", "A", "B", "C", "Siguiente X", "Error" };
                foreach (var header in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(header, FontFactory.GetFont("Arial", 11, Font.BOLD)));
                    cell.BackgroundColor = new BaseColor(230, 230, 250);
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    table.AddCell(cell);
                }

                foreach (var it in iteraciones)
                {
                    table.AddCell(it.Iteracion.ToString());
                    table.AddCell(it.X0.ToString("F6"));
                    table.AddCell(it.X1.ToString("F6"));
                    table.AddCell(it.X2.ToString("F6"));
                    table.AddCell(it.A.ToString("F6"));
                    table.AddCell(it.B.ToString("F6"));
                    table.AddCell(it.C.ToString("F6"));
                    table.AddCell(it.NextX.ToString("F6"));
                    table.AddCell(it.MargenError.ToString("F9"));
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"Ultima_Operacion_Muller_{grupo.GrupoId}.pdf");
            }
        }


        public ActionResult BorrarGrupo(int grupoId)
        {
            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();
                string queryDeleteResultados = "DELETE FROM MullerResultados WHERE GrupoId = @GrupoId";
                string queryDeleteGrupo = "DELETE FROM MullerGrupo WHERE GrupoId = @GrupoId";

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

        private List<IteracionMuller> EjecutarMuller(double x0, double x1, double x2, double tolerance, int maxIterations, Func<double, double> function)
        {
            var iteraciones = new List<IteracionMuller>();
            int iteracionActual = 1; // ✅ Inicializar correctamente la numeración

            for (int i = 1; i <= maxIterations; i++) // ✅ Comenzar en 1 en lugar de 0
            {
                double f0 = function(x0);
                double f1 = function(x1);
                double f2 = function(x2);

                if (Math.Abs(f0) < 1e-10 && Math.Abs(f1) < 1e-10 && Math.Abs(f2) < 1e-10)
                {
                    break;
                }

                double h1 = x1 - x0;
                double h2 = x2 - x1;
                double d1 = (f1 - f0) / h1;
                double d2 = (f2 - f1) / h2;
                double a = (d2 - d1) / (h2 + h1);
                double b = d2 + h2 * a;
                double c = f2;

                double discriminant = b * b - 4 * a * c;
                if (discriminant < 0)
                {
                    break;
                }

                double sqrtDisc = Math.Sqrt(discriminant);
                double denominator = (Math.Abs(b + sqrtDisc) > Math.Abs(b - sqrtDisc)) ? (b + sqrtDisc) : (b - sqrtDisc);
                double dxr = -2 * c / denominator;
                double nextX = x2 + dxr;
                double margenError = Math.Abs(nextX - x2);

                iteraciones.Add(new IteracionMuller
                {
                    Iteracion = iteracionActual, // ✅ Aquí se asigna el número de iteración
                    X0 = x0,
                    X1 = x1,
                    X2 = x2,
                    A = a,
                    B = b,
                    C = c,
                    NextX = nextX,
                    MargenError = margenError
                });

                iteracionActual++; // ✅ Aumentar número de iteración

                if (Math.Abs(margenError) < tolerance)
                {
                    break;
                }

                x0 = x1;
                x1 = x2;
                x2 = nextX;
            }

            return iteraciones;
        }


        private double EvaluarFuncion(string funcion, double x)
        {
            try
            {
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
            expr = System.Text.RegularExpressions.Regex.Replace(expr, @"e\^(\(?-?[\d\.x\+\-\*/\^\(\)]+?\)?)", "exp($1)");
            expr = System.Text.RegularExpressions.Regex.Replace(expr, @"([\w\)\.]+)\s*\^\s*([\w\(\)\.\-]+)", "Pow($1, $2)");
            return expr;
        }
    }
}