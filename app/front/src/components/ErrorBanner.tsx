// Reusable inline error. Renders nothing when there is no message.
export function ErrorBanner({ message }: { message: string | null }) {
  if (!message) return null;
  return (
    <div className="error" role="alert">
      {message}
    </div>
  );
}
