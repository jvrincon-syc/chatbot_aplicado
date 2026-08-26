// Reusable animated "assistant is typing" indicator.
export function TypingDots() {
  return (
    <div className="typing" aria-label="El asistente está escribiendo">
      <span className="typing__dot" />
      <span className="typing__dot" />
      <span className="typing__dot" />
    </div>
  );
}
