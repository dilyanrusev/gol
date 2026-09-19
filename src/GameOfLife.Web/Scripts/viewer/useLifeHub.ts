import { useCallback, useEffect, useRef, useState } from "react";
import { HubConnectionBuilder } from "@microsoft/signalr";
// Generated from the server's ILifeHub / ILifeClient / Frame by the build (see GameOfLife.Web.csproj).
import { getHubProxyFactory, getReceiverRegister } from "../generated/TypedSignalR.Client/index";
import type { ILifeHub } from "../generated/TypedSignalR.Client/GameOfLife.Web.Hubs";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";

export type StatusVariant = "secondary" | "success" | "warning" | "danger";

/** The connection's state, and nothing else. */
export interface Status {
  text: string;
  variant: StatusVariant;
}

/** A transient message about something the user just did: a refused request, a warning. */
export interface Notice {
  text: string;
  variant: "warning" | "danger";
  /** Distinguishes consecutive notices with the same text, so each one restarts the auto-hide. */
  key: number;
}

/** How long a notice stays before it hides on its own. */
export const NOTICE_MS = 6000;

export interface LifeHub {
  /** The latest frame from the server; null until the first one arrives. */
  frame: Frame | null;
  /** True once connected and initialised from the server. Controls may act only then. */
  ready: boolean;
  status: Status;
  notice: Notice | null;
  /** Shows a transient notice; it replaces the previous one and hides after NOTICE_MS. */
  notify(text: string, variant?: Notice["variant"]): void;
  dismissNotice(): void;
  /**
   * Invokes a hub method through the generated proxy, applies the frame it returns (if any) and
   * surfaces hub errors as a notice. The method name and arguments are checked against ILifeHub,
   * so a renamed or re-typed server method fails to compile here.
   */
  call<M extends keyof ILifeHub>(method: M, ...args: Parameters<ILifeHub[M]>): Promise<void>;
}

/**
 * Owns the SignalR connection for the page's lifetime. The page starts from the server's actual
 * state: after every (re)connect the current frame is pulled with Refresh rather than relying on
 * the hub's push, and an edit session lost to a reconnect is asked for again. A hidden tab tells
 * the server so and receives no frames until it is shown again, when the current frame arrives
 * with the acknowledgement.
 */
export function useLifeHub(): LifeHub {
  const [frame, setFrame] = useState<Frame | null>(null);
  const [ready, setReady] = useState(false);
  const [status, setStatus] = useState<Status>({ text: "connecting…", variant: "secondary" });
  const [notice, setNotice] = useState<Notice | null>(null);
  const hubRef = useRef<ILifeHub | null>(null);
  const frameRef = useRef<Frame | null>(null);
  const noticeKey = useRef(0);

  const applyFrame = useCallback((f: Frame) => {
    frameRef.current = f;
    setFrame(f);
  }, []);

  const notify = useCallback((text: string, variant: Notice["variant"] = "danger") => {
    setNotice({ text, variant, key: ++noticeKey.current });
  }, []);
  const dismissNotice = useCallback(() => setNotice(null), []);

  const call = useCallback(async <M extends keyof ILifeHub>(method: M, ...args: Parameters<ILifeHub[M]>): Promise<void> => {
    const hub = hubRef.current;
    if (!hub) return;
    const invoke = hub[method] as (...a: Parameters<ILifeHub[M]>) => Promise<Frame | void>;
    try {
      const result = await invoke(...args);
      if (result) applyFrame(result);
    } catch (err) {
      console.error(method, err);
      notify((err as Error).message.replace(/^.*HubException: /, ""));
    }
  }, [applyFrame, notify]);

  useEffect(() => {
    const connection = new HubConnectionBuilder().withUrl("/hubs/life").withAutomaticReconnect().build();
    const hub = getHubProxyFactory("ILifeHub").createHubProxy(connection);
    const receiver = getReceiverRegister("ILifeClient").register(connection, { receiveFrame: async (f) => applyFrame(f) });
    hubRef.current = hub;
    let disposed = false;
    // A reconnect gets a new connection, and with it the server drops our edit session; ask for it back.
    let resumeEdit = false;

    const offline = (text: string, variant: StatusVariant) => {
      setReady(false);
      setStatus({ text, variant });
    };

    // Nothing is drawn for a background tab, so nothing is sent to it. The server's answer is
    // the current frame, which brings a tab that comes back up to date at once.
    const tellVisibility = async () => {
      if (disposed || connection.state !== "Connected") return;
      try {
        applyFrame(await hub.setVisibility(!document.hidden));
      } catch (err) {
        console.error("setVisibility", err);
      }
    };
    const onVisibilityChange = () => { void tellVisibility(); };
    document.addEventListener("visibilitychange", onVisibilityChange);

    const initialise = async () => {
      setStatus({ text: "connected", variant: "success" });
      try {
        applyFrame(await hub.refresh());
        if (document.hidden) await tellVisibility();
      } catch (err) {
        console.error("refresh", err);
        offline("no state from server", "danger");
        return;
      }
      setReady(true);
      if (resumeEdit) {
        resumeEdit = false;
        const current = frameRef.current!;
        if (!current.editing) await call("beginEdit");
        else if (!current.editingByMe) notify("Another client took over editing while you were reconnecting.", "warning");
      }
    };

    connection.onreconnecting(() => {
      resumeEdit = frameRef.current?.editingByMe ?? false;
      offline("reconnecting…", "warning");
    });
    connection.onreconnected(() => { void initialise(); });
    connection.onclose(() => { if (!disposed) offline("disconnected", "danger"); });
    connection.start()
      .then(() => { if (!disposed) return initialise(); })
      .catch((err) => { console.error(err); offline("connection failed", "danger"); });

    return () => {
      disposed = true;
      document.removeEventListener("visibilitychange", onVisibilityChange);
      receiver.dispose();
      hubRef.current = null;
      void connection.stop();
    };
  }, [applyFrame, call, notify]);

  return { frame, ready, status, notice, notify, dismissNotice, call };
}
