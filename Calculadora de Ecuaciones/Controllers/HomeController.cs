using System;
using System.Linq;
using System.Web.Mvc;
using System.Data.SQLite;
using Calculadora_de_Ecuaciones.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using iTextSharp.text;
using NCalc;
using System.Security.Cryptography;
using System.Text;


namespace Calculadora_de_Ecuaciones.Controllers
{
    public class HomeController : Controller
    {
        private string GetConnectionString()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "CalculadoraBD.s3db");
            return $"Data Source={dbPath};Version=3;";
        }

        public ActionResult Contact()
        {
            return View();
        }

        public ActionResult Index()
        {
            return View();
        }

        public static class Seguridad
        {
            public static string HashPassword(string password)
            {
                // Generar una sal aleatoria
                byte[] salt = new byte[16];
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(salt);
                }

                // Derivar la clave usando PBKDF2
                var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000);
                byte[] hash = pbkdf2.GetBytes(20);

                // Combinar la sal y el hash
                byte[] hashBytes = new byte[36];
                Array.Copy(salt, 0, hashBytes, 0, 16);
                Array.Copy(hash, 0, hashBytes, 16, 20);

                // Convertir a base64 para almacenar
                return Convert.ToBase64String(hashBytes);
            }
            public static bool VerificarPassword(string passwordIngresada, string hashAlmacenado)
            {
                byte[] hashBytes = Convert.FromBase64String(hashAlmacenado);

                // Extraer la sal
                byte[] salt = new byte[16];
                Array.Copy(hashBytes, 0, salt, 0, 16);

                // Volver a generar el hash con la contraseña ingresada y la sal
                var pbkdf2 = new Rfc2898DeriveBytes(passwordIngresada, salt, 10000);
                byte[] hash = pbkdf2.GetBytes(20);

                // Comparar byte por byte
                for (int i = 0; i < 20; i++)
                {
                    if (hashBytes[i + 16] != hash[i])
                        return false;
                }

                return true;
            }
        }



        [HttpPost]
        public ActionResult Register(string nombre, string contrasena)
        {
            try
            {
                string hashedPassword = Seguridad.HashPassword(contrasena);

                using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
                {
                    conn.Open();
                    string query = "INSERT INTO LoginBaseDatos (Nombre, Contrasena) VALUES (@Nombre, @Contrasena)";
                    using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Nombre", nombre);
                        cmd.Parameters.AddWithValue("@Contrasena", hashedPassword);
                        int rowsAffected = cmd.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            return RedirectToAction("Index");
                        }
                        else
                        {
                            ViewBag.ErrorMessage = "No se ha insertado ningún usuario.";
                            return View("Contact");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = $"Error al registrar usuario: {ex.Message}";
                return View("Contact");
            }
        }


        [HttpPost]
        public ActionResult Login(string username, string password)
        {
            using (SQLiteConnection conn = new SQLiteConnection(GetConnectionString()))
            {
                conn.Open();
                string query = "SELECT Id, Nombre, Contrasena FROM LoginBaseDatos WHERE Nombre = @Nombre";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Nombre", username);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int usuarioId = reader.GetInt32(0);
                            string nombre = reader.GetString(1);
                            string hashAlmacenado = reader.GetString(2);

                            if (Seguridad.VerificarPassword(password, hashAlmacenado))
                            {
                                Session["UsuarioId"] = usuarioId;
                                Session["UserName"] = nombre;
                                return RedirectToAction("Index", "Metodos");
                            }
                        }
                    }
                }
            }

            TempData["ErrorMessage"] = "Usuario o contraseña incorrectos.";
            return RedirectToAction("Index");
        }



        public ActionResult Logout()
        {
            Session.Clear(); 
            return RedirectToAction("Index");
        }

        public ActionResult Inicio()
        {
            if (Session["UsuarioId"] != null)
            {
                return View();
            }
            return RedirectToAction("Index");
        }
    }
}