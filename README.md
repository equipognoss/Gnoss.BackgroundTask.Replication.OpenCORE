# Gnoss.BackgroundTask.Replication.OpenCORE

Aplicación de segundo plano que permite la alta disponibilidad de lectura. Se encarga de replicar las instrucciones que se han insertado en un servidor de Virtuoso en tantos servidores de Virtuoso réplica como haya configurados.

Configuración estandar de esta aplicación en el archivo docker-compose.yml: 

```yml
replicacion:
    image: replication
    env_file: .env
    environment:
     virtuosoConnectionString: ${virtuosoConnectionString}
     virtuosoConnectionString_home: ${virtuosoConnectionString_home}
     acid: ${acid}
     base: ${base}
     RabbitMQ__colaServiciosWin: ${RabbitMQ}
     RabbitMQ__colaReplicacion: ${RabbitMQ}
     ColaReplicacionMaster_ColaReplicaVirtuosoTest1: "HOST=197.166.1.21:1111;UID=admin;PWD=admin123;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
     ColaReplicacionMaster_ColaReplicaVirtuosoTest2: "HOST=196.165.1.22:1111;UID=admin;PWD=admin123;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
     ColaReplicacionMasterHome__ColaReplicaHome1: "HOST=196.165.1.23:1111;UID=admin;PWD=admin123;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
     redis__redis__ip__master: ${redis__redis__ip__master}
     redis__redis__bd: ${redis__redis__bd}
     redis__redis__timeout: ${redis__redis__timeout}
     redis__recursos__ip__master: ${redis__recursos__ip__master}
     redis__recursos__bd: ${redis__recursos_bd}
     redis__recursos__timeout: ${redis__recursos_timeout}
     redis__liveUsuarios__ip__master: ${redis__liveUsuarios__ip__master}
     redis__liveUsuarios__bd: ${redis__liveUsuarios_bd}
     redis__liveUsuarios__timeout: ${redis__liveUsuarios_timeout}
     idiomas: "es|Español,en|English"
     Servicios__urlBase: "https://servicios.test.com"
     connectionType: "0"
     intervalo: "100"
    volumes:
     - ./logs/replicacion:/app/logs
```

Se pueden consultar los posibles valores de configuración de cada parámetro aquí: https://github.com/equipognoss/Gnoss.Platform.Deploy
