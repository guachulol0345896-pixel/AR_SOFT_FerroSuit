# Infraestructura · IA · Gemini (opcional)

`ClienteGemini` solo **redacta textos** a partir de cifras ya calculadas por las reglas; no predice nada.
Implementa el puerto `IGeneradorTexto`. Se puede apagar con `"Gemini": { "Habilitado": false }` en `config/appsettings.json`.
La clave nunca va en el repositorio: se guarda en `%LocalAppData%\FerroSuite\clave-gemini.txt` o en la variable `GEMINI_API_KEY`.
Decisión pendiente del equipo: eliminarlo o dejarlo como opción apagada por defecto (auditoría, sección 8).
