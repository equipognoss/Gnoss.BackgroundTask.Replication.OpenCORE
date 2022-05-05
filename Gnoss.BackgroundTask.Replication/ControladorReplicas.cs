using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.Data;
using Es.Riam.Util;
using Es.Riam.Gnoss.Logica.BASE_BD;
using Es.Riam.Gnoss.AD.BASE_BD;
using Es.Riam.Gnoss.AD.BASE_BD.Model;
using Es.Riam.Gnoss.Logica.ParametroAplicacion;

using Es.Riam.Gnoss.Util.General;
using Es.Riam.Gnoss.Recursos;

using Es.Riam.Gnoss.Servicios;
using System.Linq;
using Es.Riam.Gnoss.RabbitMQ;
using Newtonsoft.Json;
using Es.Riam.Gnoss.AD.EntityModel;
using Es.Riam.Gnoss.Util.Configuracion;
using Es.Riam.Gnoss.CL;
using Es.Riam.Gnoss.AD.Virtuoso;
using Es.Riam.Gnoss.AD.EntityModelBASE;
using Microsoft.Extensions.DependencyInjection;
using Es.Riam.AbstractsOpen;

namespace Es.Riam.Gnoss.Win.ServicioReplicacionVirtuoso
{
    internal class ControladorReplica : ControladorServicioGnoss
    {
        #region Miembros

        /// <summary>
        /// El número de la conexión que se debe usar para actualizar virtusos.
        /// </summary>
        private string mCadenaConexion;

        /// <summary>
        /// Cadena de conexión que se debe usar para actualizar virtuoso.
        /// </summary>
        private string mCadenaConexionVirtuoso;

        /// <summary>
        /// Devuelve si la cadena de conexión para actualizar virtuoso es una BBDD Master.
        /// </summary>
        private bool mDBMaster;
        private string mExchangeName;
        private string mTablaColaReplica;
        private string mUrlIntragnoss;
        
        private DateTime mFechaEmail_UltimoErrorSintaxis;
        private int mNumErroresConexion = 0;
        protected BaseComunidadDS mBaseComunidadDS;
        protected bool mTraerFilasConEstado2 = true;
       

        #endregion

        #region Constructores

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pFicheroConfiguracionBD">Ruta al archivo de configuración de la base de datos</param>
        /// <param name="pTablaColaReplica">Nombre de la tabla de cola de esta réplica</param>
        //public ControladorReplica(string pExchangeName, string pTablaColaReplica, string pCadenaConexion,  LoggingService loggingService, EntityContext entityContext, ConfigService configService, RedisCacheWrapper redisCacheWrapper, EntityContextBASE entityContextBASE, UtilidadesVirtuoso utilidadesVirtuoso)
        public ControladorReplica(string pExchangeName, string pTablaColaReplica, string pCadenaConexion,ConfigService configService, IServiceScopeFactory scopeFactory)
            : base(scopeFactory, configService)
        {
            mExchangeName = pExchangeName;
            mTablaColaReplica = pTablaColaReplica;
            mCadenaConexion = pCadenaConexion;
        }

        #endregion

        #region Métodos generales
        private bool mReiniciarCola = false;

        public void OnShutDown()
        {
            mReiniciarCola = true;
        }

        public bool ProcesarItem(string pConsulta)
        {
            using (var scope = ScopedFactory.CreateScope())
            {
                EntityContext entityContext = scope.ServiceProvider.GetRequiredService<EntityContext>();
                UtilidadesVirtuoso utilidadesVirtuoso = scope.ServiceProvider.GetRequiredService<UtilidadesVirtuoso>();
                LoggingService loggingService = scope.ServiceProvider.GetRequiredService<LoggingService>();
                IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication = scope.ServiceProvider.GetRequiredService<IServicesUtilVirtuosoAndReplication>();
                try
                {

                    ComprobarCancelacionHilo();

                    System.Diagnostics.Debug.WriteLine($"ProcesarItem, {pConsulta}!");

                    if (!string.IsNullOrEmpty(pConsulta))
                    {
                        KeyValuePair<List<string>, bool> datosReplicacion = JsonConvert.DeserializeObject<KeyValuePair<List<string>, bool>>(pConsulta);

                        bool usarHttpPost = datosReplicacion.Value;

                        if (datosReplicacion.Key.Count > 1)
                        {
                            VirtuosoAD virtuosoAD = new VirtuosoAD(loggingService, entityContext, mConfigService, servicesUtilVirtuosoAndReplication);
                            virtuosoAD.IniciarTransaccion();

                            try
                            {
                                foreach (string consultaTransaccion in datosReplicacion.Key)
                                {
                                    try
                                    {
                                        InsertarEnVirtuoso(mCadenaConexionVirtuoso, consultaTransaccion, usarHttpPost, false, entityContext, utilidadesVirtuoso, loggingService, servicesUtilVirtuosoAndReplication);
                                    }
                                    catch
                                    {
                                        throw;
                                    }
                                }
                                virtuosoAD.TerminarTransaccion(true);
                            }
                            catch (Exception)
                            {
                                virtuosoAD.TerminarTransaccion(false);
                                throw;
                            }
                        }
                        else
                        {
                            string consulta = datosReplicacion.Key.First();
                            if (!string.IsNullOrEmpty(consulta))
                            {
                                InsertarEnVirtuoso(mCadenaConexionVirtuoso, datosReplicacion.Key.First(), usarHttpPost, false, entityContext, utilidadesVirtuoso, loggingService, servicesUtilVirtuosoAndReplication);
                            }
                        }
                    }
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        public override void RealizarMantenimiento(EntityContext entityContext, EntityContextBASE entityContextBASE, UtilidadesVirtuoso utilidadesVirtuoso, LoggingService loggingService, RedisCacheWrapper redisCacheWrapper, GnossCache gnossCache, VirtuosoAD virtuosoAD, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            CargarDatosConexionVirutoso(loggingService);

            RealizarMantenimientoRabbitMQ(loggingService);
        }

        public void RealizarMantenimientoRabbitMQ(LoggingService loggingService, bool reintentar = true)
        {
            bool usarReplicacionRabbit = true;

            string bdRabbit = RabbitMQClient.BD_REPLICACION;
            
            if (usarReplicacionRabbit && mConfigService.ExistRabbitConnection(bdRabbit))
            {
                RabbitMQClient.ReceivedDelegate funcionProcesarItem = new RabbitMQClient.ReceivedDelegate(ProcesarItem);
                RabbitMQClient.ShutDownDelegate funcionShutDown = new RabbitMQClient.ShutDownDelegate(OnShutDown);

                RabbitMQClient rMQ = new RabbitMQClient(bdRabbit, mTablaColaReplica, loggingService, mConfigService, mExchangeName);
                
                try
                {
                    rMQ.ObtenerElementosDeCola(funcionProcesarItem, funcionShutDown);
                    mReiniciarCola = false;
                }
                catch (Exception ex)
                {
                    mReiniciarLecturaRabbit = true;
                    loggingService.GuardarLogError(ex);
                }
            }
        }

        private void ProcesarFilasDeTransaccion(string pInfoExtra, List<BaseComunidadDS.ColaReplicacionRow> pFilasTransaccion, EntityContext entityContext, EntityContextBASE entityContextBASE, UtilidadesVirtuoso utilidadesVirtuoso, LoggingService loggingService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            string[] parametros = pInfoExtra.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

            int numeroTransacciones = 0;

            foreach (string parametro in parametros)
            {
                if (parametro.StartsWith("##transactions##"))
                {
                    int.TryParse(parametro.Replace("##transactions##", ""), out numeroTransacciones);
                }
            }

            ReplicacionCN replicacionCN = new ReplicacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);

            BaseComunidadDS baseComunidadDS = null;
            List<BaseComunidadDS.ColaReplicacionRow> listaFilasOriginal = null;

            if (numeroTransacciones > 0 && pFilasTransaccion.Count < numeroTransacciones)
            {
                //Faltan transacciones que no se han cargado de la base de datos, las cargo
                baseComunidadDS = replicacionCN.ObtenerElementosColaReplicacionMismaTransaccion(mTablaColaReplica, 5, pInfoExtra);
                listaFilasOriginal = pFilasTransaccion;
                pFilasTransaccion = baseComunidadDS.ColaReplicacion.ToList();
            }

            short estado = 5;

            VirtuosoAD virtuosoAD = new VirtuosoAD(loggingService, entityContext, mConfigService, servicesUtilVirtuosoAndReplication);
            virtuosoAD.IniciarTransaccion();

            try
            {
                foreach (BaseComunidadDS.ColaReplicacionRow filaReplica in pFilasTransaccion)
                {
                    ComprobarCancelacionHilo();

                    try
                    {
                        if (!ProcesarFila(filaReplica, false, entityContext, entityContextBASE, utilidadesVirtuoso, loggingService, servicesUtilVirtuosoAndReplication))
                        {
                            estado = filaReplica.Estado;
                            throw new Exception($"Error al replicar instrucción {filaReplica.OrdenEjecucion}. Query: {filaReplica.Consulta}. Transaccion: {filaReplica.InfoExtra}");
                        }
                    }
                    catch
                    {
                        estado = filaReplica.Estado;
                        throw;
                    }
                }
                virtuosoAD.TerminarTransaccion(true);
            }
            catch (Exception)
            {
                virtuosoAD.TerminarTransaccion(false);

                if (estado == 5 || estado == 0)
                {
                    estado = 1;
                }
                
                throw;
            }
            finally
            {
                replicacionCN.ActualizarEstadoCola(pFilasTransaccion.Select(fila => fila.OrdenEjecucion).ToList(), estado, mTablaColaReplica);

                if (baseComunidadDS != null)
                {
                    // Libero el dataset temporal
                    baseComunidadDS.Dispose();

                    // Marco las filas del dataset original como procesadas
                    foreach (BaseComunidadDS.ColaReplicacionRow filaCola in listaFilasOriginal)
                    {
                        filaCola.Estado = estado;
                    }
                }
            }
        }

        /// <summary>
        /// A partir del número de conexión obtiene los demás datos necesarios para actualizar virtuoso.
        /// </summary>
        private void CargarDatosConexionVirutoso(LoggingService loggingService)
        {
            try
            {
                //DataRow[] filasUrlIntragnoss = GestorParametroAplicacionDS.ParametroAplicacion.Select("Parametro = 'UrlIntragnoss'");
                List<ParametroAplicacion> filasUrlIntragnoss = GestorParametroAplicacionDS.ParametroAplicacion.Where(parametroApp=>parametroApp.Parametro.Equals("UrlIntragnoss")).ToList();

                if (filasUrlIntragnoss.Count > 0)
                {
                    mUrlIntragnoss = filasUrlIntragnoss[0].Valor;
                }
                else
                {
                    throw new Exception("No hay una UrlIntragnoss configurada para el entorno '" + mFicheroConfiguracionBDOriginal + "', tabla '" + mTablaColaReplica + "'.");
                }

                //List<ConfiguracionBBDD> configuracionesBBDD = GestorParametroAplicacionDS.ListaConfiguracionBBDD.Select("NumConexion = " + mNumConexion);

                if(string.IsNullOrEmpty(mCadenaConexion))
                {
                    throw new Exception("No hay una ninguna BBDD con numConexion '" + mCadenaConexion + "', en el entorno '" + mFicheroConfiguracionBDOriginal + "', tabla '" + mTablaColaReplica + "'.");
                }

            mCadenaConexionVirtuoso = mCadenaConexion;

            mDBMaster = true;

            //ParametroAplicacionDS.ConfiguracionBBDD.Clear();
        }
            catch (Exception ex)
            {
                // Escribir en LOG el error y la BBDD
                string error = loggingService.DevolverCadenaError(ex, VersionEnsamblado());
                GuardarLog(error, loggingService);
                throw;
            }
        }

        /// <summary>
        /// Procesa una fila de la cola de replicación
        /// </summary>
        /// <param name="pFilaCola">Fila de la cola de replicación</param>
        /// <param name="pReintentarSiFalla">Verdad si se debe de reintentar la consulta en caso de error</param>
        /// <returns>Verdad si se ha procesado correctamente, falso en caso contrario</returns>
        private bool ProcesarFila(BaseComunidadDS.ColaReplicacionRow pFilaCola, bool pReintentarSiFalla, EntityContext entityContext, EntityContextBASE entityContextBASE, UtilidadesVirtuoso utilidadesVirtuoso, LoggingService loggingService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            bool actualizarCola = (pFilaCola.Estado < 2);
            Exception excepcion = null;
            bool excepcionLanzada = false;
            bool hayError = false;
            BaseComunidadCN baseComunidadCN = new BaseComunidadCN(mFicheroConfiguracionBDBase, -1, entityContext, loggingService, entityContextBASE, mConfigService, servicesUtilVirtuosoAndReplication);

            try
            {
                bool usarHttpPost = (!pFilaCola.IsUsarHttpPostNull() && pFilaCola.UsarHttpPost);

                InsertarEnVirtuoso(mCadenaConexionVirtuoso, pFilaCola.Consulta, usarHttpPost, pReintentarSiFalla, entityContext, utilidadesVirtuoso, loggingService, servicesUtilVirtuosoAndReplication);

                if (pFilaCola.Estado == 2)
                {
                    actualizarCola = true;
                    excepcionLanzada = false;
                    //La replicación había fallado y ahora vuelve a funcionar, informo de ello. 
                    EnviarErrorYGuardarLog(new ExcepcionDeReplicacion("Mensaje informativo: La replicación de virtuoso para la conexión '" + mFicheroConfiguracionBDOriginal + "' y la tabla '" + mTablaColaReplica + "' vuelve a funcionar CORRECTAMENTE. "), "Servicio de replicación de virtuoso restituido", loggingService);
                }

                //Éxito
                pFilaCola.Estado = 5;
                hayError = false;
            }
            catch (Exception ex)
            {
                hayError = true;
                string mensaje = "Excepción: " + ex.ToString() + "\n\n\tTraza: " + ex.StackTrace + "\n\nFila: " + pFilaCola["OrdenEjecucion"];

                //TODO: Comprobar que funciona
                if ((ex.InnerException != null) && (ex.InnerException.StackTrace.Contains("SQLSTATE: 37000") || ex.InnerException.Message.Contains("SQLSTATE: 37000") || ex.Message.Contains("Virtuoso 37000") || ex.InnerException.Message.Contains("syntax error") || ex.InnerException.StackTrace.Contains("syntax error")) || ex.InnerException.Message.Contains("Empty string is not a valid argument"))
                {
                    //Error de sintaxis
                    //Si no se ha mandado email, enviamos uno
                    if (mFechaEmail_UltimoErrorSintaxis == null || mFechaEmail_UltimoErrorSintaxis.AddDays(1) < DateTime.Now)
                    {
                        EnviarErrorYGuardarLog(ex, "Servicio de replicación de virtuoso restituido", loggingService);
                        mFechaEmail_UltimoErrorSintaxis = DateTime.Now;
                    }
                    else
                    {
                        GuardarLog(ex, loggingService);
                    }

                    //Cambiamos el estado de la fila a 4 para que no la re-intente.
                    pFilaCola.Estado = 4;
                }
                else
                {
                    if (actualizarCola)
                    {
                        //Marco la fila con errores
                        pFilaCola.Estado++;
                    }
                    //Si falla insertar en una fila, guardar log
                    string mensajeError = "Error al replicar la transacción: " + pFilaCola.OrdenEjecucion + ". ";

                    //Compruebo si había error previamente. Si es así, no envío emails continuamente. 
                    if (!excepcionLanzada)
                    {
                        excepcion = ex;
                        if (pFilaCola.Estado == 2)
                        {
                            mensajeError += "La replicación de virtuoso para " + mTablaColaReplica + " está PARADA. ";
                            // Creo la excepción, pero se lanza al final del foreach para actualizar el estado de la fila en BD
                            excepcion = new ExcepcionDeReplicacion(mensajeError, ex);
                            excepcionLanzada = true;
                        }
                        else
                        {
                            GuardarLog(mensajeError, loggingService);
                        }
                    }
                }
            }

            if (hayError)
            {
                if (excepcion != null)
                {
                    //La replicación ha fallado, lanzo la excepción
                    throw excepcion;
                }
            }

            baseComunidadCN.Dispose();

            return !hayError;
        }

        /// <summary>
        /// Hacemos 3 intentos de la insercción en Virtuoso
        /// </summary>
        /// <param name="pConsulta"></param>
        private void InsertarEnVirtuoso(string pConexionVirtuoso, string pConsulta, bool pUsarHttpPost, bool pReintentarSiFalla, EntityContext entityContext, UtilidadesVirtuoso utilidadesVirtuoso, LoggingService loggingService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            VirtuosoAD virtuosoAD = null;
            try
            {
                virtuosoAD = new VirtuosoAD(loggingService, entityContext, mConfigService, servicesUtilVirtuosoAndReplication, pConexionVirtuoso);
                //Insertar en virtuoso

                if (pUsarHttpPost)
                {
                    virtuosoAD.ActualizarVirtuoso(pConsulta);
                }
                else
                {
                    virtuosoAD.ActualizarVirtuoso_ClienteTradicional(pConsulta);
                }
            }
            catch (Exception ex)
            {
                string error = loggingService.DevolverCadenaError(ex, VersionEnsamblado());
                GuardarLog(error, loggingService);

                if (pReintentarSiFalla)
                {
                    //Destruimos el objeto FacetadoCN porque si lo reutilizamos da fallos
                    virtuosoAD.Dispose();

                    //Cerramos las conexiones
                    ControladorConexiones.CerrarConexiones(false);

                    //Realizamos una consulta ask a virtuoso para comprobar si está funcionando
                    while (!utilidadesVirtuoso.VirtuosoOperativo(mCadenaConexionVirtuoso))
                    {
                        //Dormimos 30 segundos
                        Thread.Sleep(2000);
                    }

                    virtuosoAD = new VirtuosoAD(loggingService, entityContext, mConfigService, servicesUtilVirtuosoAndReplication);
                    //Insertar en virtuoso
                    if (pUsarHttpPost)
                    {
                        virtuosoAD.ActualizarVirtuoso(pConsulta);
                    }
                    else
                    {
                        virtuosoAD.ActualizarVirtuoso_ClienteTradicional(pConsulta);
                    }
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Método para enviar al servicio Modulo Base los parámetros enviados como parámetro extra
        /// </summary>
        /// <param name="pInfoExtra">Parámetros extra para que procese el servicio módulo Base.</param>
        private void EnviarFilasServicioBase(string pInfoExtra,EntityContext entityContext, EntityContextBASE entityContextBASE, LoggingService loggingService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            string[] delimiter = { "|;|%|;|" };
            //Insertamos en el base los nuevos parámetros
            foreach (string nuevaFila in pInfoExtra.Split(delimiter, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    //Creamos la fila para procesar por el base:
                    string[] fila = nuevaFila.Split('|');

                    //TablaBaseProyectoID
                    int tablaBaseProyectoID = int.Parse(fila[0]);

                    //Tag
                    string tag = fila[1];

                    //Tipo de acción (0 agregado) (1 eliminado)
                    short accion = short.Parse(fila[2]);

                    //Prioridad de procesado por el servicio base.
                    int prioridadBase = int.Parse(fila[3]);



                    BaseRecursosComunidadDS baseRecursosComDS = new BaseRecursosComunidadDS();

                    #region Marcar agregado

                    BaseRecursosComunidadDS.ColaTagsComunidadesRow filaColaTagsDocs = baseRecursosComDS.ColaTagsComunidades.NewColaTagsComunidadesRow();

                    filaColaTagsDocs.Estado = (short)EstadosColaTags.EnEspera;
                    filaColaTagsDocs.FechaPuestaEnCola = DateTime.Now;
                    filaColaTagsDocs.TablaBaseProyectoID = tablaBaseProyectoID;
                    filaColaTagsDocs.Tags = tag;
                    filaColaTagsDocs.Tipo = accion;
                    filaColaTagsDocs.Prioridad = (short)prioridadBase;

                    long estadoCargaID = -1;
                    short tipoAccionCarga = -1;
                    if (fila.Length > 4 && long.TryParse(fila[4], out estadoCargaID))
                    {
                        filaColaTagsDocs.EstadoCargaID = estadoCargaID;
                    }
                    if (fila.Length > 5 && short.TryParse(fila[5], out tipoAccionCarga))
                    {
                        filaColaTagsDocs.TipoAccionCarga = tipoAccionCarga;
                    }


                    baseRecursosComDS.ColaTagsComunidades.AddColaTagsComunidadesRow(filaColaTagsDocs);

                    #endregion

                    BaseComunidadCN brComCN = new BaseComunidadCN(mFicheroConfiguracionBDBase, -1, entityContext, loggingService, entityContextBASE, mConfigService, servicesUtilVirtuosoAndReplication);
                    brComCN.InsertarFilasEnRabbit("ColaTagsComunidades", baseRecursosComDS);
                    brComCN.Dispose();

                    baseRecursosComDS.Dispose();
                }
                catch (Exception ex)
                {
                    GuardarLog("Error al agregar al base. Conexión '" + mFicheroConfiguracionBDOriginal + "', tabla '" + mTablaColaReplica + "'" + Environment.NewLine + ex.Message + Environment.NewLine + ex.StackTrace, loggingService);
                }
            }
        }

        /// <summary>
        /// Carga los mantenimientos pendientes
        /// </summary>
        /// <param name="pNombreTabla"></param>
        /// <returns>Verdad si hay algún elemento que procesar</returns>
        protected bool CargarDatos(string pNombreTabla, EntityContext entityContext, LoggingService loggingService, IServicesUtilVirtuosoAndReplication servicesUtilVirtuosoAndReplication)
        {
            bool hayElementosEnCola = false;

            int numMaxItems = 200;

            // La primera vez que arranca el servicio, se trae las filas que habían fallado antes. 
            short estadoMax = 2;
            if (mTraerFilasConEstado2)
            {
                estadoMax = 3;
                mTraerFilasConEstado2 = false;
            }

            //Recursos de comunidad
            ReplicacionCN replicacionCN = new ReplicacionCN(entityContext, loggingService, mConfigService, servicesUtilVirtuosoAndReplication);

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

            return hayElementosEnCola;
        }

        protected override ControladorServicioGnoss ClonarControlador()
        {
            return new ControladorReplica(mExchangeName, mTablaColaReplica, mCadenaConexion, mConfigService, ScopedFactory);
        }

        public override string VersionEnsamblado()
        {
            AssemblyName nombreDelEnsamblado = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            return nombreDelEnsamblado.Version.ToString();
        }

        #endregion

        #region Propiedades


        public static int HorasBorrado { get; set; } = 1;

        #endregion
    }
}
