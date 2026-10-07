import { Component, useEffect, type ReactNode } from 'react';
import { useRouteError } from 'react-router';
import { reportError } from '../lib/errors';
import { Card } from './ui';

/**
 * Shown in place of a page that crashed. Inside the layout the menu keeps working; `centered` is for crashes
 * with no layout around them (the layout itself, or the sign-in pages).
 */
export function RouteCrash({ centered = false }: { centered?: boolean }) {
  const error = useRouteError();
  useEffect(() => reportError(error), [error]);
  return centered ? (
    <div className="centered">
      <CrashMessage />
    </div>
  ) : (
    <CrashMessage />
  );
}

/** The last line of defence, around the whole portal, for a crash outside any page. */
export class AppCrashBoundary extends Component<{ children: ReactNode }, { crashed: boolean }> {
  state = { crashed: false };

  static getDerivedStateFromError() {
    return { crashed: true };
  }

  componentDidCatch(error: unknown) {
    reportError(error);
  }

  render() {
    return this.state.crashed ? (
      <div className="centered">
        <CrashMessage />
      </div>
    ) : (
      this.props.children
    );
  }
}

function CrashMessage() {
  return (
    <Card className="crash-card">
      <h1>Something went wrong on this page</h1>
      <p className="muted">We&apos;ve let the team know. Reload the page to try again; anything you saved is safe.</p>
      <div className="row">
        <button type="button" className="btn btn-primary" onClick={() => window.location.reload()}>
          Reload the page
        </button>
        <a className="btn btn-secondary" href="/">
          Go to Home
        </a>
      </div>
    </Card>
  );
}
