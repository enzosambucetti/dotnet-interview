import * as signalR from "@microsoft/signalr";

const defaultInitialRetryDelaysMs = [1000, 2000, 5000, 10000, 30000];
const defaultReconnectDelaysMs = [0, 2000, 5000, 10000, 30000];

function trimTrailingSlash(value) {
  return value.endsWith("/") ? value.slice(0, -1) : value;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

export function createTodoSignalRClient({
  baseUrl = "http://localhost:5083",
  onTodoUpdated,
  logger = console,
  initialRetryDelaysMs = defaultInitialRetryDelaysMs,
  reconnectDelaysMs = defaultReconnectDelaysMs,
} = {}) {
  const hubUrl = `${trimTrailingSlash(baseUrl)}/hubs/todo-updates`;
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect(reconnectDelaysMs)
    .configureLogging(signalR.LogLevel.Information)
    .build();

  if (onTodoUpdated) {
    connection.on("todoUpdated", onTodoUpdated);
  }

  connection.onreconnecting((error) => {
    logger.warn?.("SignalR reconnecting.", error);
  });

  connection.onreconnected((connectionId) => {
    logger.info?.("SignalR reconnected.", { connectionId });
  });

  connection.onclose((error) => {
    logger.warn?.("SignalR connection closed.", error);
  });

  async function start() {
    let attempt = 0;

    while (connection.state === signalR.HubConnectionState.Disconnected) {
      try {
        await connection.start();
        logger.info?.("SignalR connected.", { hubUrl });
        return connection;
      } catch (error) {
        const retryDelay =
          initialRetryDelaysMs[Math.min(attempt, initialRetryDelaysMs.length - 1)];

        logger.warn?.("SignalR initial connection failed. Retrying.", {
          attempt: attempt + 1,
          retryDelay,
          error,
        });

        attempt += 1;
        await delay(retryDelay);
      }
    }

    return connection;
  }

  async function stop() {
    await connection.stop();
  }

  return {
    connection,
    start,
    stop,
  };
}
