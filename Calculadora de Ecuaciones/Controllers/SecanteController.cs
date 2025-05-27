using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using Calculadora_de_Ecuaciones.Models;
using iTextSharp.text.pdf;
using iTextSharp.text;
using NCalc;
using System.Linq;

namespace Calculadora_de_Ecuaciones.Controllers
{
    public class SecanteController : Controller
    {
        private string GetConnectionString()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "CalculadoraBD.s3db");
            return $"Data Source={dbPath};Version=3;";
        }

        public ActionResult Index()
        {
            return View(new SecanteModel());
        }
        [HttpPost]
        public ActionResult Index(SecanteModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Funcion))
            {
                model.Mensaje = "Error: Debes ingresar una función.";
                model.TipoMensaje = "error";
                return View(model);
            }

            var resultados = new List<ResultadoIteracion>();
            double x0 = model.X0;
            double x1 = model.X1;
            int maxIter = model.MaxIter;
            double tol = model.Tolerancia;
            bool convergencia = false;
            int usuarioId = ObtenerUsuarioActual();

            int grupoId;

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"INSERT INTO SecanteGrupo 
            (Funcion, X0, X1, Fecha, UsuarioId, MaxIter, Tolerancia) 
            VALUES 
            (@Funcion, @X0, @X1, @Fecha, @UsuarioId, @MaxIter, @Tolerancia); 
            SELECT last_insert_rowid();";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@Funcion", model.Funcion);
                    cmdGrupo.Parameters.AddWithValue("@X0", x0);
                    cmdGrupo.Parameters.AddWithValue("@X1", x1);
                    cmdGrupo.Parameters.AddWithValue("@Fecha", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmdGrupo.Parameters.AddWithValue("@UsuarioId", usuarioId);
                    cmdGrupo.Parameters.AddWithValue("@MaxIter", maxIter);
                    cmdGrupo.Parameters.AddWithValue("@Tolerancia", tol);

                    grupoId = Convert.ToInt32(cmdGrupo.ExecuteScalar());
                }

                string queryResultados = @"INSERT INTO SecanteResultados 
            (GrupoId, Iteracion, Xi, Xi_1, XiMas1, Error, UsuarioId, Timestamp) 
            VALUES 
            (@GrupoId, @Iteracion, @Xi, @Xi_1, @XiMas1, @Error, @UsuarioId, @Timestamp)";

                int iter = 0;
                double error = double.MaxValue;

                while (error > tol && iter < maxIter)
                {
                    double fx0 = EvaluarFuncion(model.Funcion, x0);
                    double fx1 = EvaluarFuncion(model.Funcion, x1);

                    if (fx1 - fx0 == 0)
                    {
                        model.Mensaje = "Error: División por cero.";
                        model.TipoMensaje = "error";
                        return View(model);
                    }

                    double x2 = x1 - fx1 * (x1 - x0) / (fx1 - fx0);
                    error = Math.Abs(x2 - x1);

                    resultados.Add(new ResultadoIteracion
                    {
                        GrupoId = grupoId,
                        Iteracion = iter + 1,
                        Xi = x0,
                        Xi_1 = x1,
                        XiMas1 = x2,
                        Error = error,
                        Timestamp = DateTime.Now
                    });

                    using (SQLiteCommand cmdRes = new SQLiteCommand(queryResultados, conn))
                    {
                        cmdRes.Parameters.AddWithValue("@GrupoId", grupoId);
                        cmdRes.Parameters.AddWithValue("@Iteracion", iter + 1);
                        cmdRes.Parameters.AddWithValue("@Xi", x0);
                        cmdRes.Parameters.AddWithValue("@Xi_1", x1);
                        cmdRes.Parameters.AddWithValue("@XiMas1", x2);
                        cmdRes.Parameters.AddWithValue("@Error", error);
                        cmdRes.Parameters.AddWithValue("@UsuarioId", usuarioId);
                        cmdRes.Parameters.AddWithValue("@Timestamp", DateTime.Now);
                        cmdRes.ExecuteNonQuery();
                    }

                    x0 = x1;
                    x1 = x2;
                    iter++;

                    if (error < tol)
                    {
                        convergencia = true;
                        break;
                    }
                }
            }

            if (!convergencia)
            {
                model.Mensaje = $"Advertencia: No se encontró la raíz dentro del número máximo de iteraciones ({maxIter}).";
                model.TipoMensaje = "warning";
            }

            model.Resultados = resultados;
            return View(model);
        }


        public ActionResult Historial()
        {
            int usuarioId = ObtenerUsuarioActual();
            ViewBag.UsuarioId = usuarioId;

            var grupos = new List<SecanteGrupoViewModel>();

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();
                string query = @"
            SELECT g.GrupoId, g.Funcion, g.X0, g.X1, g.Fecha, g.UsuarioId, g.MaxIter, g.Tolerancia,
                   r.Iteracion, r.Xi, r.Xi_1, r.XiMas1, r.Error 
            FROM SecanteGrupo g
            LEFT JOIN SecanteResultados r ON g.GrupoId = r.GrupoId
            WHERE g.UsuarioId = @UsuarioId
            ORDER BY g.Fecha DESC, r.Iteracion ASC";

                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UsuarioId", usuarioId);

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int grupoId = reader.GetInt32(0);
                            var grupo = grupos.Find(g => g.GrupoId == grupoId);
                            if (grupo == null)
                            {
                                grupo = new SecanteGrupoViewModel
                                {
                                    GrupoId = grupoId,
                                    Funcion = reader.GetString(1),
                                    X0 = reader.GetDouble(2),
                                    X1 = reader.GetDouble(3),
                                    MaxIter = reader.GetInt32(6),
                                    Tolerancia = reader.GetDouble(7),
                                    Fecha = reader.GetDateTime(4),
                                    UsuarioId = reader.GetInt32(5),
                                    Iteraciones = new List<ResultadoIteracion>()

                                };
                                grupos.Add(grupo);
                            }

                            if (!reader.IsDBNull(8))
                            {
                                grupo.Iteraciones.Add(new ResultadoIteracion
                                {
                                    Iteracion = reader.GetInt32(8),
                                    Xi = reader.GetDouble(9),
                                    Xi_1 = reader.GetDouble(10),
                                    XiMas1 = reader.GetDouble(11),
                                    Error = reader.GetDouble(12),
                                    Timestamp = grupo.Fecha
                                });
                            }
                        }
                    }
                }
            }

            return View(grupos);
        }



        public ActionResult BorrarGrupo(int grupoId)
        {
            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                // ✅ Eliminar todas las iteraciones del grupo específico
                string queryDeleteResultados = "DELETE FROM SecanteResultados WHERE GrupoId = @GrupoId;";
                string queryDeleteGrupo = "DELETE FROM SecanteGrupo WHERE GrupoId = @GrupoId;";

                using (SQLiteCommand cmd = new SQLiteCommand(queryDeleteResultados, conn))
                {
                    cmd.Parameters.AddWithValue("@GrupoId", grupoId);
                    cmd.ExecuteNonQuery();
                }

                using (SQLiteCommand cmd = new SQLiteCommand(queryDeleteGrupo, conn))
                {
                    cmd.Parameters.AddWithValue("@GrupoId", grupoId);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Mensaje"] = $"Grupo {grupoId} eliminado correctamente.";
            return RedirectToAction("Historial");
        }

        public ActionResult GenerarPDF(int grupoId)
        {
            SecanteGrupoViewModel grupo = null;

            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();

                string queryGrupo = @"
            SELECT GrupoId, Funcion, X0, X1, Fecha, MaxIter, Tolerancia
            FROM SecanteGrupo 
            WHERE GrupoId = @GrupoId";

                using (SQLiteCommand cmdGrupo = new SQLiteCommand(queryGrupo, conn))
                {
                    cmdGrupo.Parameters.AddWithValue("@GrupoId", grupoId);
                    using (SQLiteDataReader reader = cmdGrupo.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            grupo = new SecanteGrupoViewModel
                            {
                                GrupoId = reader.GetInt32(0),
                                Funcion = reader.GetString(1),
                                X0 = reader.GetDouble(2),
                                X1 = reader.GetDouble(3),
                                Fecha = reader.GetDateTime(4),
                                MaxIter = reader.GetInt32(5),
                                Tolerancia = reader.GetDouble(6),
                                Iteraciones = new List<ResultadoIteracion>()
                            };
                        }
                    }
                }

                if (grupo == null)
                    return Content("No hay operaciones recientes para generar el PDF.");

                string queryResultados = @"
            SELECT Iteracion, Xi, Xi_1, XiMas1, Error 
            FROM SecanteResultados 
            WHERE GrupoId = @GrupoId 
            ORDER BY Iteracion ASC";

                using (SQLiteCommand cmdResultados = new SQLiteCommand(queryResultados, conn))
                {
                    cmdResultados.Parameters.AddWithValue("@GrupoId", grupo.GrupoId);
                    using (SQLiteDataReader reader = cmdResultados.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            grupo.Iteraciones.Add(new ResultadoIteracion
                            {
                                Iteracion = reader.GetInt32(0),
                                Xi = reader.GetDouble(1),
                                Xi_1 = reader.GetDouble(2),
                                XiMas1 = reader.GetDouble(3),
                                Error = reader.GetDouble(4),
                                Timestamp = grupo.Fecha
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
                PdfWriter writer = PdfWriter.GetInstance(doc, ms);

                doc.Open();

                string imagePath = Server.MapPath("~/Content/Fotos/Umg.png");
                Image logo = Image.GetInstance(imagePath);
                logo.ScaleAbsolute(60f, 60f);
                logo.SetAbsolutePosition(doc.LeftMargin, doc.PageSize.Height - 70);
                doc.Add(logo);


                Paragraph titulo = new Paragraph("Reporte de Método de la Secante", new Font(Font.FontFamily.HELVETICA, 16, Font.BOLD));
                titulo.Alignment = Element.ALIGN_CENTER;
                titulo.SpacingAfter = 20f;
                doc.Add(titulo);

                doc.Add(new Paragraph($"Función: {grupo.Funcion}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Valores iniciales: x0 = {grupo.X0}, x1 = {grupo.X1}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Tolerancia: {grupo.Tolerancia.ToString("0.############################")}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Máximo de iteraciones: {grupo.MaxIter}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph($"Fecha: {grupo.Fecha}", FontFactory.GetFont("Arial", 12)));
                doc.Add(new Paragraph("\n"));

                PdfPTable table = new PdfPTable(5);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 1.5f, 2f, 2f, 2f, 2f });

                string[] headers = { "Iteración", "x0", "x1", "x2", "Error" };
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
                    table.AddCell(new PdfPCell(new Phrase(resultado.Xi.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.Xi_1.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.XiMas1.ToString("F6"))));
                    table.AddCell(new PdfPCell(new Phrase(resultado.Error.ToString("F9"))));
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"Ultima_Operacion_Secante_{grupo.GrupoId}.pdf");
            }
        }



        private int ObtenerUsuarioActual()
        {
            return Session["UsuarioId"] != null ? Convert.ToInt32(Session["UsuarioId"]) : 0;
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
            expr = Regex.Replace(expr, @"e\^(\(?-?[\d\.x\+\-\*/\^\(\)]+?\)?)", "exp($1)");
            expr = Regex.Replace(expr, @"([\w\)\.]+)\s*\^\s*([\w\(\)\.\-]+)", "Pow($1, $2)");
            return expr;
        }
    }
}