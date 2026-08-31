export function TypingDots() {
  return (
    <div className="typing-wrap">
      <div className="typing" aria-label="El asistente está escribiendo">
        <span className="typing__dot" />
        <span className="typing__dot" />
        <span className="typing__dot" />
      </div>
      <span className="typing-label">Consultando información verificada...</span>
    </div>
  );
}
