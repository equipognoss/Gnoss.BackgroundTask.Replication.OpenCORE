using Es.Riam.Gnoss.AD.EntityModel;
using Es.Riam.Gnoss.AD.EntityModelBASE;
using Es.Riam.Gnoss.CL;
using Es.Riam.Gnoss.Elementos.Suscripcion;
using Es.Riam.Gnoss.Servicios;
using Es.Riam.Gnoss.Util.Configuracion;
using Es.Riam.Gnoss.Util.General;
using Es.Riam.Gnoss.Win.ServicioReplicacionVirtuoso;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gnoss.BackgroundTask.Replication
{
    public class ReplicationWorker : Worker
    {
        private ConfigService mConfigService;
        private ILogger mlogger;
        private ILoggerFactory mLoggerFactory;
        public ReplicationWorker(ConfigService configService, IServiceScopeFactory scopeFactory, ILogger<ReplicationWorker> logger, ILoggerFactory loggerFactory) : base(logger, scopeFactory)
        {
            mConfigService = configService;
            mlogger = logger;
            mLoggerFactory = loggerFactory;
        }

        protected override List<ControladorServicioGnoss> ObtenerControladores()
        {
            ControladorServicioGnoss.INTERVALO_SEGUNDOS = mConfigService.ObtenerIntervalo();
            Conexion.ServicioWindows = true;
            int horasBorrado = 1;
            if(mConfigService.ObtenerHorasBorrado() != 0)
            {
                horasBorrado = mConfigService.ObtenerHorasBorrado();
            }

            ControladorReplica.HorasBorrado = horasBorrado;
            List<ControladorServicioGnoss> controladores = new List<ControladorServicioGnoss>();
            foreach(var item in mConfigService.ObtenerColasReplicacionMasterHome())
            {
                controladores.Add(new ControladorReplica("ColaReplicacionMasterHome", item.Key, item.Value, mConfigService, ScopedFactory, mLoggerFactory.CreateLogger<ControladorReplica>(), mLoggerFactory));
            }
            foreach (var item in mConfigService.ObtenerColasReplicacionMaster())
            {
                controladores.Add(new ControladorReplica("ColaReplicacionMaster", item.Key, item.Value, mConfigService, ScopedFactory, mLoggerFactory.CreateLogger<ControladorReplica>(), mLoggerFactory));
            }

            return controladores;
        }
    }
}
