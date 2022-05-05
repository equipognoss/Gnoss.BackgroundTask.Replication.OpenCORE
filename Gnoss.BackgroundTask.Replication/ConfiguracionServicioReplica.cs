using System;
using System.Collections.Generic;
using System.Text;
using Es.Riam.Gnoss.Util.General;
using System.Xml;
using System.IO;
using System.Reflection;

namespace Es.Riam.Gnoss.Win.ServicioReplicacionVirtuoso
{
    public class ConfiguracionServicioReplica : ConfiguracionServicios
    {

        #region Miembros

        /// <summary>
        /// Fichero de configuración y tabla para la replica
        /// </summary>
        private Dictionary<string, List<string>> mTablasReplica;

        /// <summary>
        /// Fichero de configuración y tabla para la transacción
        /// </summary>
        private Dictionary<string, List<string>> mTablasTransacion;

        #endregion

        #region Constructores

        public ConfiguracionServicioReplica()
            : base()
        {

        }

        #endregion

        #region Propiedades

        /// <summary>
        /// Obtiene la lista con las rutas de los ficheros de configuración de las conexiones a los virtuosos y las tablas de las que va a leer los datos.
        /// </summary>
        public Dictionary<string, List<string>> TablasColaReplica
        {
            get
            {
                if (this.mTablasReplica == null)
                {
                    this.CargarFicherosConfiguracionBD();
                }

                return mTablasReplica;
            }
        }

        /// <summary>
        /// Devuelve el diccionario con el fichero de configuración y la tabla de la que se van a cargar los datos para las transacciones
        /// </summary>
        public Dictionary<string, List<string>> TablasColaTransacciones
        {
            get
            {
                if (this.mTablasTransacion == null)
                {
                    this.CargarFicherosConfiguracionBD();
                }
                return mTablasTransacion;
            }
        }

        #endregion

        #region Metodos

        protected override void CargarFicherosConfiguracionBD()
        {
            if (this.mRaizXml == null)
            {
                this.CargarDocumento();
            }

            //Contiene ls archivos de conexión de las replicas y sus tablas
            mTablasReplica = new Dictionary<string, List<string>>();

            //Contiene los archivos de conexión y las tablas de las transacciones
            mTablasTransacion = new Dictionary<string, List<string>>();

            XmlNode xmlConexiones = this.mRaizXml.SelectSingleNode("servicio-gnoss");

            if (xmlConexiones.ChildNodes != null)
            {
                //Recorremos las conexiones
                foreach (XmlNode bdNode in xmlConexiones.SelectNodes("conexiones"))
                {
                    //Valores para el serivio de transaciones
                    //string archivoConexionTransaciones = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + Path.DirectorySeparatorChar + "config" + Path.DirectorySeparatorChar + bdNode.Attributes["base"].Value;
                    string archivoConexionTransaciones = bdNode.Attributes["base"].Value;
                    string tablaTransaciones = bdNode.Attributes["value"].Value;

                    //Obtenemos las conexiones para las replicas
                    foreach (XmlNode conexionNode in bdNode.SelectNodes("conexion"))
                    {
                        string nombre = conexionNode.SelectSingleNode("nombre").Attributes["valor"].Value;
                        string tablasReplicas = conexionNode.SelectSingleNode("archivoconexion").Attributes["tabla"].Value;

                        //Agregamos las tablas a la lista de Replicas
                        if (conexionNode.SelectSingleNode("archivoconexion").Attributes["activo"].Value.Equals("1"))
                        {
                            string numConexion = conexionNode.SelectSingleNode("archivoconexion").Attributes["valor"].Value;
                            int tempNumConexionVirtuoso = 0;
                            if (!int.TryParse(numConexion, out tempNumConexionVirtuoso))
                            {
                                throw new Exception("El atributo 'valor' del nodo 'archivoconexion' no está correctamente configurado. Hay que poner un entero que representa el valor de la columna 'numConexion' de la tabla 'ConfiguracionBBDD' en la que está la configuración del virtuoso al que tiene que apuntar el hilo '" + nombre + "'");
                            }

                            string archivoConexionYTablaReplica = archivoConexionTransaciones + "|" + tablaTransaciones;
                            string tablasReplicasYNumConexion = tablasReplicas + "|" + numConexion;
                            if (mTablasReplica.ContainsKey(archivoConexionYTablaReplica))
                            {
                                if (!mTablasReplica[archivoConexionYTablaReplica].Contains(tablasReplicasYNumConexion))
                                {
                                    mTablasReplica[archivoConexionYTablaReplica].Add(tablasReplicasYNumConexion);
                                }
                            }
                            else
                            {
                                List<string> temp = new List<string>();
                                temp.Add(tablasReplicasYNumConexion);
                                mTablasReplica.Add(archivoConexionYTablaReplica, temp);
                            }
                        }

                        //Agregamos las tablas a las transacciones
                        //Debemos insertar el config, la tabla de la que van a leer y las tablas donde deben insertar.
                        //Formato: config|tabla, listaTablasReplica

                        string archivoConexionYTabla = archivoConexionTransaciones + "|" + tablaTransaciones;
                        if (mTablasTransacion.ContainsKey(archivoConexionYTabla))
                        {
                            if (!mTablasTransacion[archivoConexionYTabla].Contains(tablasReplicas))
                            {
                                mTablasTransacion[archivoConexionYTabla].Add(tablasReplicas);
                            }
                        }
                        else
                        {
                            List<string> temp = new List<string>();
                            temp.Add(tablasReplicas);
                            mTablasTransacion.Add(archivoConexionYTabla, temp);
                        }
                    }
                }
            }
        }

        #endregion
    }
}
