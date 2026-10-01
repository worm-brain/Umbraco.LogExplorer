import { css, html, nothing, state, type TemplateResult } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { SourceResponseModel } from "../api/index.js";
import { LOG_EXPLORER_QUERY_CONTEXT, type LogExplorerQueryContext } from "../query/query.context.js";
import type { LogExplorerViewState } from "../query/view-state.js";
import { RequestLoader, type RequestFn, type RequestState } from "../shared/request-loader.js";

/**
 * What every Overview panel shares (UI brief §4.13, §4.15): it reads the query bar's
 * `queryState` and the active source from `LogExplorerQueryContext`, sends one request whenever
 * either changes the request, and draws its `uui-box` with a loader bar, an error banner with
 * Retry, an Approximate tag, or a line saying the source lacks the feature.
 *
 * A subclass names the feature it needs, builds its request and renders its rows. Panels read
 * `queryState`, not `state`, so chips the source cannot run and a disallowed native query never
 * reach the server (ADR 0016).
 */
export abstract class LogExplorerOverviewPanelBase<TRequest, TResult> extends UmbLitElement {
  /** The loader's state; every change re-renders the panel. */
  @state()
  protected _data: RequestState<TResult> = { status: "idle" };

  /** Display name of the active source when it lacks {@link feature}; `undefined` otherwise. */
  @state()
  protected _unsupportedSource: string | undefined;

  /** The view state as queries run it, for subclasses that render from it (level set, range). */
  @state()
  protected _viewState: LogExplorerViewState | undefined;

  /** The source the panel queries; `undefined` until resolved or when it lacks the feature. */
  protected _source: SourceResponseModel | undefined;

  /** The query context, for focus actions. */
  protected _context: LogExplorerQueryContext | undefined;

  #loader = new RequestLoader<TRequest, TResult>(
    (alias, request, signal) => this.fetchData(alias, request, signal),
    (data) => (this._data = data),
  );
  #requestKey?: string;

  /** The `capabilities.features` entry the panel needs, such as `histogram`. */
  protected abstract readonly feature: string;

  /** Sends the panel's request; see {@link RequestFn}. */
  protected abstract fetchData: RequestFn<TRequest, TResult>;

  /**
   * Builds the request for a view state. Two states that build the same request do not refetch.
   *
   * @param state - The view state as queries run it.
   * @returns The request body.
   */
  protected abstract buildRequest(state: LogExplorerViewState): TRequest;

  /**
   * Called when the active source changes, after the panel has decided whether it supports the
   * feature, for panels that load something per source.
   *
   * @param _source - The new source, or `undefined`.
   */
  protected onSourceChanged(_source: SourceResponseModel | undefined): void {}

  constructor() {
    super();
    this.consumeContext(LOG_EXPLORER_QUERY_CONTEXT, (context) => {
      this._context = context;
      this.observe(
        context?.queryState,
        (viewState) => {
          this._viewState = viewState;
          this.#requery();
        },
        "_observeState",
      );
      this.observe(
        context?.activeSource,
        (source) => {
          const supported = source ? source.capabilities.features.includes(this.feature) : true;
          this._source = supported ? source : undefined;
          this._unsupportedSource = supported ? undefined : source?.displayName;
          this.onSourceChanged(source);
          this.#requery();
        },
        "_observeActiveSource",
      );
    });
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    // Nothing should land on a view the user has left; the context observers re-emit on
    // reconnect, which queries again.
    this.#loader.reset();
    this.#requestKey = undefined;
  }

  #requery(): void {
    if (!this._source || !this._viewState) {
      this.#loader.reset();
      this.#requestKey = undefined;
      return;
    }

    const request = this.buildRequest(this._viewState);
    const key = JSON.stringify([this._source.alias, request]);
    if (key === this.#requestKey) return;
    this.#requestKey = key;
    this.#loader.load(this._source.alias, request);
  }

  /**
   * Draws the panel's box around the subclass's content.
   *
   * @param headline - The box headline.
   * @param unsupportedText - The line shown when the source lacks the feature.
   * @param errorText - The banner text for a failed request, given its message.
   * @param approximate - Whether the result is approximate (sampled or budget-limited).
   * @param body - The rows; called only when the panel has a result or a request has finished.
   * @returns The template.
   */
  protected renderPanel(
    headline: string,
    unsupportedText: (source: string) => string,
    errorText: (message: string) => string,
    approximate: boolean,
    body: () => TemplateResult | typeof nothing,
  ) {
    const { status, error, result } = this._data;
    return html`
      <uui-box headline=${headline}>
        ${
          approximate
            ? html`<uui-tag
                slot="header-actions"
                look="secondary"
                title=${this.localize.term("logExplorer_histogramApproximateHint")}
                >${this.localize.term("logExplorer_histogramApproximate")}</uui-tag
              >`
            : nothing
        }
        <section class="panel" aria-label=${headline} aria-busy=${status === "loading" ? "true" : "false"}>
          ${status === "loading" ? html`<uui-loader-bar class="loader"></uui-loader-bar>` : nothing}
          ${
            this._unsupportedSource !== undefined
              ? html`<p class="empty">${unsupportedText(this._unsupportedSource)}</p>`
              : html`
                  ${
                    status === "error"
                      ? html`<div class="error" role="alert">
                          <span>${errorText(error ?? "")}</span>
                          <uui-button
                            look="secondary"
                            compact
                            label=${this.localize.term("logExplorer_retry")}
                            @click=${() => this.#loader.retry()}
                          ></uui-button>
                        </div>`
                      : nothing
                  }
                  <div class=${status === "loading" ? "body dimmed" : "body"}>
                    ${result !== undefined || status === "loaded" ? body() : nothing}
                  </div>
                `
          }
        </section>
      </uui-box>
    `;
  }

  /** Box, states and the button-row pattern every panel uses. */
  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        min-width: 0;
      }

      uui-box {
        position: relative;
        height: 100%;
        --uui-box-default-padding: var(--uui-size-space-3) var(--uui-size-space-4);
      }

      .panel {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-3);
      }

      .loader {
        position: absolute;
        inset: 0 0 auto 0;
      }

      .body {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-3);
        transition: opacity 120ms ease-out;
      }

      .body.dimmed {
        opacity: 0.5;
      }

      .error {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        border: 1px solid var(--uui-color-danger-standalone);
        border-radius: var(--uui-border-radius);
        color: var(--uui-color-danger-standalone);
      }

      .empty {
        margin: 0;
        color: var(--uui-color-text-alt);
      }

      uui-tag[slot="header-actions"] {
        font-size: var(--uui-type-small-size);
      }

      ul.rows {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      /* A focus row: a full-width button with the count drawn over its right edge. uui-button
         sizes its label slot to the content, so the count cannot sit inside it and still align;
         it ignores the pointer, so the whole row clicks (the fields panel's pattern). */
      .focus-row {
        position: relative;
        container-type: inline-size;
        --log-explorer-overview-count-space: 8ch;
      }

      .focus-row uui-button {
        width: 100%;
        --uui-button-content-align: left;
      }

      .focus-label {
        display: block;
        max-width: calc(100cqi - var(--log-explorer-overview-count-space));
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        text-align: start;
      }

      .count {
        position: absolute;
        inset-block: 0;
        right: var(--uui-size-space-2);
        display: flex;
        align-items: center;
        font-weight: bold;
        font-variant-numeric: tabular-nums;
        pointer-events: none;
      }
    `,
  ];
}
