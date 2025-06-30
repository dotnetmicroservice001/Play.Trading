# Play.Trading - Trading Microservice

The Trading microservice manages user trades within the Play Economy system. It coordinates complex operations like item purchases using a **Saga pattern** and **state machine**, ensuring consistency across distributed services.

## 🔁 Features

- Handles item purchases and user-to-user trades
- Orchestrates workflows using **Saga with state machines**
- Integrates with **Inventory**, **Catalog**, and **Identity** services
- Uses **SignalR** to provide real-time trade updates to frontend clients
- Secured via **OAuth 2.0** and **JWT tokens** issued by the Identity microservice

## 🧱 Tech Stack

- ASP.NET Core
- MassTransit + State Machine (Saga)
- SignalR
- RabbitMQ (event messaging)
- Duende IdentityServer (OAuth 2.0 / OpenID Connect)

```bash
export version=1.0.2
export GH_OWNER=dotnetmicroservice001
export GH_PAT="ghp_YourRealPATHere"
docker build --secret id=GH_OWNER --secret id=GH_PAT -t play.trading:$version .
```

## Run Docker Image
```bash 
docker run -it --rm -p 5006:5006 --name identity -e MongoDbSettings__Host=mongo -e RabbitMQSettings__Host=rabbitmq --network playinfra_default play.trading:$version
```