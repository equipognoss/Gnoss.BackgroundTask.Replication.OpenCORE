using System;
using System.Web;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Data;
using Es.Riam.Util;
using System.Xml;
using Es.Riam.Gnoss.Logica.BASE_BD;
using Es.Riam.Gnoss.AD.BASE_BD;
using Es.Riam.Gnoss.AD.BASE_BD.Model;
using Es.Riam.Gnoss.Logica.ParametroAplicacion;
using Es.Riam.Gnoss.Logica.Facetado;
using Es.Riam.Gnoss.Recursos;
using Es.Riam.Gnoss.AD.EntityModel;
using Es.Riam.Gnoss.Util.General;
using Es.Riam.Gnoss.Util.Configuracion;
using Es.Riam.AbstractsOpen;
using Microsoft.Extensions.Logging;
using Es.Riam.Gnoss.Elementos.Suscripcion;

namespace Es.Riam.Gnoss.Win.ServicioReplicacionVirtuoso
{
    internal abstract class ControladorBase
    {
        #region Enumeraciones

        /// <summary>
        /// Enumeración para representar el estado del servicio en el log
        /// </summary>
        public enum LogStatus
        {
            Inicio, Parada, Correcto, Error, LimiteSobrepasado
        }

        #endregion

        #region Miembros

        protected string mConfiguracionBase = "";

        protected string mConfiguracionAcid = "";

        //esto aqui, quitar del resto de sitios

        /// <summary>
        /// Intervalo de tiempo que espera el proceso entre ejecuciones
        /// </summary>
        public static int INTERVALO_SEGUNDOS;

        /// <summary>
        /// Ruta al archivo de configuración de la base de datos
        /// </summary>
        protected string mFicheroConfiguracionBD;

        /// <summary>
        /// Ruta que se usara para los ficheros de log
        /// </summary>
        protected string ficheroLog;

        protected BaseComunidadDS mBaseComunidadDS;

        

        protected bool mTraerFilasConEstado2 = true;

        /// <summary>
        /// Minuto en el que se realiza el checkpoint
        /// </summary>
        protected int mMinutoCheckPoint = -1;

        /// <summary>
        /// Intervalo de tiempo para la realización del siguiente checkpoint
        /// </summary>
        protected int mIntervalo = -1;

        private EntityContext mEntityContext;
        private LoggingService mLogginService;
        private ConfigService mConfigService;
        private IServicesUtilVirtuosoAndReplication mServicesUtilVirtuosoAndReplication;
        private ILogger mlogger;
        private ILoggerFactory mLoggerFactory;

        #endregion

        #region Constructores

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pFicheroConfiguracionBD">Ruta al archivo de configuración de la base de datos</param>
        public ControladorBase(string pFicheroConfiguracionBD, EntityContext entityContext, LoggingService loggingService, ConfigService configService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication, ILogger<ControladorBase> logger, ILoggerFactory loggerFactory)
        {
            mEntityContext = entityContext;
            mLogginService = loggingService;
            mConfigService = configService;
            mServicesUtilVirtuosoAndReplication = servicesUtilVirtuosoAndReplication;
            mConfiguracionAcid = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + Path.DirectorySeparatorChar + "config" + Path.DirectorySeparatorChar + pFicheroConfiguracionBD + "/acid";
            mConfiguracionBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + Path.DirectorySeparatorChar + "config" + Path.DirectorySeparatorChar + pFicheroConfiguracionBD + "/base";

            mFicheroConfiguracionBD = pFicheroConfiguracionBD;

            string nombreLog = Path.GetFileNameWithoutExtension(mFicheroConfiguracionBD);
            string directorioLog = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + Path.DirectorySeparatorChar + "logs";
            mlogger = logger;
            mLoggerFactory = loggerFactory;

            if (!Directory.Exists(directorioLog))
            {
                Directory.CreateDirectory(directorioLog);
            }
            ficheroLog = directorioLog + Path.DirectorySeparatorChar + nombreLog;

            //Cargamos los datos del del checkpoint y del intervalo
            CargarDatosCkeckPoint(mConfiguracionBase.Substring(0, mConfiguracionBase.LastIndexOf("/")));
        }

        private void CargarDatosCkeckPoint(string pXmlConfig)
        {
            //Obtenemos los datos del checkpoint en el base.
            XmlDocument xDoc = new XmlDocument();
            xDoc.Load(pXmlConfig);
            XmlNodeList config = xDoc.GetElementsByTagName("config");
            XmlNodeList configBase = ((XmlElement)config[0]).GetElementsByTagName("base");
            if (configBase.Count > 0 && ((XmlElement)configBase[0]).GetElementsByTagName("checkpoint") != null && ((XmlElement)configBase[0]).GetElementsByTagName("checkpoint").Count > 0)
            {
                XmlNodeList nCheckPoint = ((XmlElement)configBase[0]).GetElementsByTagName("checkpoint");
                mMinutoCheckPoint = int.Parse(nCheckPoint[0].InnerText.Split('|')[0]);
                mIntervalo = int.Parse(nCheckPoint[0].InnerText.Split('|')[1]);
            }
        }

        #endregion

        #region Métodos generales

        #region Públicos

        /// <summary>
        /// Carga los mantenimientos pendientes
        /// </summary>
        /// <param name="pNombreTabla"></param>
        /// <returns>Verdad si hay algún elemento que procesar</returns>
        protected bool CargarDatos(string pNombreTabla)
        {
            bool hayElementosEnCola = false;

            int numMaxItems = 200;

            try
            {
                // La primera vez que arranca el servicio, se trae las filas que habían fallado antes. 
                short estadoMax = 2;
                if (mTraerFilasConEstado2)
                {
                    estadoMax = 3;
                    mTraerFilasConEstado2 = false;
                }

                //Recursos de comunidad
                ReplicacionCN replicacionCN = new ReplicacionCN(mEntityContext, mLogginService, mConfigService, mServicesUtilVirtuosoAndReplication, mLoggerFactory.CreateLogger<ReplicacionCN>(), mLoggerFactory);

                //pNombreTabla = tabla de la que trae las querys para insertar en las replicaciones
                if (string.IsNullOrEmpty(pNombreTabla))
                {
                    mBaseComunidadDS = replicacionCN.ObtenerElementosPendientesColaReplicacion(numMaxItems, estadoMax);
                }
                else
                {
                    mBaseComunidadDS = replicacionCN.ObtenerElementosPendientesColaReplicacion(numMaxItems, pNombreTabla, estadoMax);
                }
                replicacionCN.Dispose();

                hayElementosEnCola = ((mBaseComunidadDS != null) && (mBaseComunidadDS.ColaReplicacion.Rows.Count > 0));

            }
            catch (Exception) { }

            return hayElementosEnCola;
        }

   

        #endregion


        #endregion

    }
}
