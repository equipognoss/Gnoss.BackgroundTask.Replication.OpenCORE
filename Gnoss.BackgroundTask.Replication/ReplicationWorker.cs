using Es.Riam.Gnoss.AD.EntityModel;
using Es.Riam.Gnoss.AD.EntityModelBASE;
using Es.Riam.Gnoss.CL;
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
        public ReplicationWorker(ILogger<Worker> logger,ConfigService configService, IServiceScopeFactory scopeFactory) : base(logger, scopeFactory)
        {
            mConfigService = configService;
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
                controladores.Add(new ControladorReplica("ColaReplicacionMasterHome", item.Key, item.Value, mConfigService, ScopedFactory));
            }
            foreach (var item in mConfigService.ObtenerColasReplicacionMaster())
            {
                controladores.Add(new ControladorReplica("ColaReplicacionMaster", item.Key, item.Value, mConfigService, ScopedFactory));
            }

            return controladores;
        }
    }
}
