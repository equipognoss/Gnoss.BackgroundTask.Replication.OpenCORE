![](https://content.gnoss.ws/imagenes/proyectos/personalizacion/7e72bf14-28b9-4beb-82f8-e32a3b49d9d3/cms/logognossazulprincipal.png)

# Gnoss.BackgroundTask.Replication.OpenCORE

Aplicación de segundo plano que permite la alta disponibilidad de lectura. Se encarga de replicar las instrucciones que se han insertado en un servidor de Virtuoso en tantos servidores de Virtuoso réplica como haya configurados.

Este servicio escucha tantas colas de replicación como se hayan configurado en sus variables de configuración. Por ejemplo, si partimos de esta configuración: 

```yml
ColaReplicacionMaster_ColaReplicaVirtuosoTest1: "HOST=192.168.2.20:1111;UID=dba;PWD=dba;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
ColaReplicacionMaster_ColaReplicaVirtuosoTest2: "HOST=192.168.2.21:1111;UID=dba;PWD=dba;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
ColaReplicacionMasterHome__ColaReplicaHome1: "HOST=192.168.2.30:1111;UID=dba;PWD=dba;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
```

Significa que este servicio va a escuchar tres colas: 
* ColaReplicaVirtuosoTest1
* ColaReplicaVirtuosoTest2
* ColaReplicaHome1

Los mensajes que lleguen a cada una de esas colas, se insertarán en el servidor de virtuoso especificado para cada una de ellas. 

¿Qué mensajes van a llegar a cada una de esas colas? Los mensajes que se inserten en los exchange asociados. ColaReplicacionMaster es el exchange a el que se van a vincular las colas ColaReplicaVirtuosoTest1 y ColaReplicaVirtuosoTest2, y ColaReplicacionMasterHome es el exchange que se va a vincular a la cola ColaReplicaHome1. Cualquier mensaje que se inserte en el exchange, se enviará a cada una de las colas asociadas. 

La Web o el API enviarán un mensaje cada vez que se ejecute cualquier instrucción SPARQL de inserción, modificación o eliminación de triples sobre un grafo de comunidad al exchange ColaReplicacionMaster (recursos, personas, grupos, paginasCMS...), y si es sobre un grafo de usuario (mensajes o comentarios a recursos) enviará un mensaje al exchange ColaReplicacionMasterHome. 

¿Qué arquitectura refleja esta configuración? Esta configuración refleja una arquitectura con 5 servidores de virtuosos, un servidor maestro para los grafos de comunidad, 2 servidores réplica para los grafos de comunidad, un servidor maestro para los grafos de usuarios y un servidor réplica para los grafos de usuarios. 


Configuración estandar de esta aplicación en el archivo docker-compose.yml: 

```yml
replicacion:
    image: gnoss/replication
    env_file: .env
    environment:
     virtuosoConnectionString: ${virtuosoConnectionString}
     virtuosoConnectionString_home: ${virtuosoConnectionString_home}
     acid: ${acid}
     base: ${base}
     RabbitMQ__colaServiciosWin: ${RabbitMQ}
     RabbitMQ__colaReplicacion: ${RabbitMQ}
     ColaReplicacionMaster_ColaReplicaVirtuosoTest1: "HOST=192.168.2.5:1111;UID=dba;PWD=dba;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
     ColaReplicacionMaster_ColaReplicaVirtuosoTest2: "HOST=192.168.2.6:1111;UID=dba;PWD=dba;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
     ColaReplicacionMasterHome__ColaReplicaHome1: "HOST=192.168.2.5:1111;UID=dba;PWD=dba;Pooling=true;Max Pool Size=10;Connection Lifetime=15000"
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
     - ./logs/replication:/app/logs
```

Se pueden consultar los posibles valores de configuración de cada parámetro aquí: https://github.com/equipognoss/Gnoss.Platform.Deploy
