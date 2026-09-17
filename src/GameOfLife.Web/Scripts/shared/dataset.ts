/** Reads the configuration Razor puts on a mount element's data-* attributes. */
export function readConfig(element: HTMLElement) {
  const data = element.dataset;
  return {
    string(key: string): string {
      const value = data[key];
      if (value === undefined) throw new Error(`Missing data attribute '${key}' on #${element.id}.`);
      return value;
    },
    number(key: string): number {
      const value = Number(this.string(key));
      if (!Number.isFinite(value)) throw new Error(`Data attribute '${key}' on #${element.id} is not a number.`);
      return value;
    },
  };
}
