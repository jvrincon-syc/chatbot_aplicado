// Reusable header with the ambient blue aurora. Title/subtitle are props so other
// screens can reuse the same treatment.
export function ChatHeader({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <header className="header">
      <div className="header__aurora" aria-hidden="true" />
      <div className="header__row">
        <span className="header__mark" aria-hidden="true" />
        <div>
          <h1 className="header__title">{title}</h1>
          <p className="header__subtitle">{subtitle}</p>
        </div>
      </div>
    </header>
  );
}
