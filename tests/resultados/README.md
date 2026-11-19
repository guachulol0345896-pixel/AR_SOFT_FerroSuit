# Resultados de las pruebas contra SQL Server

Aquí escribe `dotnet run --project tests/FerroSuite.Pruebas -- --bd` un archivo
`resultados_bd_<fecha>_<hora>.txt` cada vez que se ejecutan las pruebas de persistencia
en un equipo Windows con SQL Server Express LocalDB.

El archivo es la evidencia verificable: indica fecha, equipo, versión de .NET y sistema,
y el resultado (PASA/FALLA) de cada prueba. Los datos usados son sintéticos y se crean en
bases temporales `FerroSuite_Pruebas_<id>` que se borran al terminar.

Esta carpeta está vacía en la entrega porque esas pruebas no se pudieron ejecutar en el
entorno donde se preparó el código (Linux, sin SQL Server). Ver `docs/persistencia.md`.
