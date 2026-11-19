# Activa la interfaz local (lo) dentro de un espacio de red aislado (unshare -rn), para la prueba sin internet.
import socket, fcntl, struct
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
ifr = struct.pack('16sH', b'lo', 0)
flags = struct.unpack('16sH', fcntl.ioctl(s, 0x8913, ifr))[1]
fcntl.ioctl(s, 0x8914, struct.pack('16sH', b'lo', flags | 1))
print("lo arriba")
