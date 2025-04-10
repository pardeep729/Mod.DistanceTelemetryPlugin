import os
import socket
from azure.eventhub import EventHubProducerClient, EventData

from dotenv import load_dotenv
load_dotenv()

EVENT_HUB_CONNECTION_STR = os.getenv("EVENT_HUB_CONN_STR", "")
EVENT_HUB_NAME = os.getenv("EVENT_HUB_NAME", "")

# Setup UDP Listener
UDP_IP = "0.0.0.0"  # Listen on all interfaces
UDP_PORT = 12345
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind((UDP_IP, UDP_PORT))
print(f"Listening on UDP port {UDP_PORT}...")

# Setup Event Hub Producer
producer = EventHubProducerClient.from_connection_string(
    conn_str=EVENT_HUB_CONNECTION_STR,
    eventhub_name=EVENT_HUB_NAME
)

if __name__ == '__main__':
    while True:
        # Receive bytes via UDP
        data, addr = sock.recvfrom(65535) 
        message = data.decode('utf-8').strip()
        try:
            # Display in console
            # print(f"Received from {addr}: {message}")

            # Send to Event Hub
            # TODO: Send more than 1 message per batch
            batch = producer.create_batch()
            batch.add(EventData(message))
            producer.send_batch(batch)
            # print("✅ Sent to Event Hub")
        except UnicodeDecodeError:
            print(f"Received non-UTF-8 data from {addr}: {data}")
        except Exception as e:
            print(f"❌ Error: {e}")