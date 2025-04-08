import socket

UDP_IP = "0.0.0.0"  # Listen on all interfaces
UDP_PORT = 12345

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind((UDP_IP, UDP_PORT))

print(f"Listening on UDP port {UDP_PORT}...")

if __name__ == '__main__':
    while True:
        data, addr = sock.recvfrom(65535)
        try:
            print(f"Received from {addr}: {data.decode('utf-8')}")
        except UnicodeDecodeError:
            print(f"Received non-UTF-8 data from {addr}: {data}")