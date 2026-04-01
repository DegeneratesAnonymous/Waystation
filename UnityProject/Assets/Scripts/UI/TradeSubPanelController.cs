// TradeSubPanelController.cs
// World → Trade sub-tab panel (UI-017).
//
// Displays:
//   1. Standing Buy Orders  — list of auto-buy rules; add/edit/delete per rule.
//                             Fields: resource, max price, max quantity.
//   2. Standing Sell Orders — same structure for auto-sell rules.
//                             Fields: resource, min price, max quantity.
//   3. Trade History Log    — last 50 transactions; read-only.
//                             Columns: tick, type, resource, qty, price/unit, total, faction, ship.
//
// Call Refresh(StationState, TradeSystem) to sync with live data.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// World → Trade sub-tab panel.  Extends <see cref="VisualElement"/> so it can
    /// be added directly to the side-panel drawer.
    /// </summary>
    public class TradeSubPanelController : VisualElement
    {
        // ── USS class names ──────────────────────────────────────────────────────

        private const string PanelClass         = "ws-trade-panel";
        private const string SectionHeaderClass = "ws-trade-panel__section-header";
        private const string RowClass           = "ws-trade-panel__order-row";
        private const string HistoryRowClass    = "ws-trade-panel__history-row";
        private const string FormClass          = "ws-trade-panel__order-form";
        private const string AddBtnClass        = "ws-trade-panel__add-btn";
        private const string ActionBtnClass     = "ws-trade-panel__action-btn";
        private const string EmptyClass         = "ws-trade-panel__empty";

        // ── Known tradeable resources ─────────────────────────────────────────────

        private static readonly string[] KnownResources =
            { "food", "parts", "oxygen", "ice", "power" };

        // ── Internal state ───────────────────────────────────────────────────────

        private readonly ScrollView    _scroll;
        private readonly VisualElement _listRoot;

        private StationState _station;
        private TradeSystem  _trade;

        // Form state: which section ("buy" or "sell") and which resource are being edited,
        // or null if no form is open.
        private string _formSection;   // "buy" | "sell" | null
        private string _formEditResource; // resource being edited, null = new order

        // ── Constructor ──────────────────────────────────────────────────────────

        public TradeSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            Add(_scroll);

            _listRoot = _scroll.contentContainer;
            _listRoot.style.flexDirection = FlexDirection.Column;
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the trade panel from live station data.
        /// Call on mount and again on every relevant tick.
        /// </summary>
        public void Refresh(StationState station, TradeSystem trade)
        {
            _station = station;
            _trade   = trade;

            // Preserve form state across refreshes so an open form survives a tick.
            RebuildList();
        }

        // ── Internal: list rebuild ───────────────────────────────────────────────

        private void RebuildList()
        {
            _listRoot.Clear();

            if (_station == null) return;

            // 1. Standing Buy Orders ──────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("Standing Buy Orders",
                new Color(0.40f, 0.80f, 0.50f, 1f)));
            BuildOrderSection("buy", _station.standingBuyOrders, "Max Price");

            // 2. Standing Sell Orders ─────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("Standing Sell Orders",
                new Color(0.90f, 0.65f, 0.30f, 1f)));
            BuildOrderSection("sell", _station.standingSellOrders, "Min Price");

            // 3. Trade History ────────────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("Trade History (last 50)",
                new Color(0.65f, 0.75f, 0.90f, 1f)));
            BuildHistorySection();
        }

        // ── Section header ───────────────────────────────────────────────────────

        private VisualElement BuildSectionHeader(string title, Color color)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.fontSize                = 10;
            header.style.color                   = color;
            header.style.paddingTop              = 10;
            header.style.paddingBottom           = 3;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            return header;
        }

        // ── Standing order section ────────────────────────────────────────────────

        private void BuildOrderSection(string section, List<StandingOrder> orders, string priceLabel)
        {
            bool formOpenHere = (_formSection == section);

            if (orders.Count == 0 && !formOpenHere)
            {
                var empty = new Label($"No {section} orders configured.");
                empty.AddToClassList(EmptyClass);
                empty.style.color      = new Color(0.55f, 0.60f, 0.70f, 1f);
                empty.style.fontSize   = 10;
                empty.style.paddingTop = 4;
                _listRoot.Add(empty);
            }
            else
            {
                foreach (var order in orders)
                {
                    bool editing = formOpenHere && _formEditResource == order.resource;
                    if (editing)
                        _listRoot.Add(BuildOrderForm(section, order.resource, order.limitPrice,
                                                     order.amount, priceLabel));
                    else
                        _listRoot.Add(BuildOrderRow(section, order, priceLabel));
                }
            }

            // Show inline "add" form when triggered
            if (formOpenHere && _formEditResource == null)
                _listRoot.Add(BuildOrderForm(section, null, 0f, 0f, priceLabel));

            // "Add order" button — always visible unless the add form is already open
            if (!(formOpenHere && _formEditResource == null))
            {
                string capturedSection = section;
                _listRoot.Add(BuildAddButton($"+ Add {section} order", () =>
                {
                    _formSection      = capturedSection;
                    _formEditResource = null;
                    RebuildList();
                }));
            }
        }

        // ── Order row ────────────────────────────────────────────────────────────

        private VisualElement BuildOrderRow(string section, StandingOrder order, string priceLabel)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection    = FlexDirection.Row;
            row.style.alignItems       = Align.Center;
            row.style.paddingTop       = 5;
            row.style.paddingBottom    = 5;
            row.style.paddingLeft      = 8;
            row.style.paddingRight     = 8;
            row.style.marginBottom     = 3;
            row.style.backgroundColor  = new Color(0.14f, 0.18f, 0.24f, 0.9f);
            row.style.borderTopLeftRadius     = 4;
            row.style.borderTopRightRadius    = 4;
            row.style.borderBottomLeftRadius  = 4;
            row.style.borderBottomRightRadius = 4;

            // Resource label (capitalised)
            var resLabel = new Label(Capitalize(order.resource));
            resLabel.style.fontSize   = 11;
            resLabel.style.color      = new Color(0.90f, 0.92f, 0.95f, 1f);
            resLabel.style.width      = 60;
            resLabel.style.flexShrink = 0;
            row.Add(resLabel);

            // Price label
            var priceDetail = new Label($"{priceLabel}: {order.limitPrice:F1}");
            priceDetail.style.fontSize   = 10;
            priceDetail.style.color      = new Color(0.70f, 0.78f, 0.85f, 1f);
            priceDetail.style.flexGrow   = 1;
            row.Add(priceDetail);

            // Qty label
            var qtyDetail = new Label($"Qty: {order.amount:F0}");
            qtyDetail.style.fontSize  = 10;
            qtyDetail.style.color     = new Color(0.70f, 0.78f, 0.85f, 1f);
            qtyDetail.style.width     = 55;
            qtyDetail.style.flexShrink = 0;
            row.Add(qtyDetail);

            // Edit button
            string capturedSection  = section;
            string capturedResource = order.resource;

            row.Add(BuildSmallButton("Edit", new Color(0.25f, 0.50f, 0.80f, 1f), () =>
            {
                _formSection      = capturedSection;
                _formEditResource = capturedResource;
                RebuildList();
            }));

            // Delete button
            row.Add(BuildSmallButton("Del", new Color(0.70f, 0.22f, 0.18f, 1f), () =>
            {
                if (_trade == null || _station == null) return;
                if (capturedSection == "buy")  _trade.RemoveBuyOrder(_station, capturedResource);
                else                            _trade.RemoveSellOrder(_station, capturedResource);
                if (_formSection == capturedSection && _formEditResource == capturedResource)
                {
                    _formSection      = null;
                    _formEditResource = null;
                }
                RebuildList();
            }));

            return row;
        }

        // ── Inline add/edit form ──────────────────────────────────────────────────

        private VisualElement BuildOrderForm(string section, string editResource,
                                             float existingPrice, float existingAmount,
                                             string priceLabel)
        {
            var form = new VisualElement();
            form.AddToClassList(FormClass);
            form.style.flexDirection    = FlexDirection.Column;
            form.style.paddingTop       = 6;
            form.style.paddingBottom    = 6;
            form.style.paddingLeft      = 8;
            form.style.paddingRight     = 8;
            form.style.marginBottom     = 4;
            form.style.backgroundColor  = new Color(0.12f, 0.17f, 0.22f, 0.95f);
            form.style.borderTopLeftRadius     = 4;
            form.style.borderTopRightRadius    = 4;
            form.style.borderBottomLeftRadius  = 4;
            form.style.borderBottomRightRadius = 4;
            form.style.borderTopWidth    = 1;
            form.style.borderTopColor    = section == "buy"
                ? new Color(0.30f, 0.60f, 0.40f, 0.6f)
                : new Color(0.70f, 0.50f, 0.20f, 0.6f);

            // ── Resource selector ────────────────────────────────────────────────
            var resRow = new VisualElement();
            resRow.style.flexDirection = FlexDirection.Row;
            resRow.style.alignItems    = Align.Center;
            resRow.style.marginBottom  = 5;
            form.Add(resRow);

            var resLabel = new Label("Resource:");
            resLabel.style.fontSize  = 10;
            resLabel.style.color     = new Color(0.70f, 0.78f, 0.85f, 1f);
            resLabel.style.width     = 65;
            resLabel.style.flexShrink = 0;
            resRow.Add(resLabel);

            var resDropdown = new DropdownField(new List<string>(KnownResources), 0);
            if (!string.IsNullOrEmpty(editResource))
            {
                int idx = Array.IndexOf(KnownResources, editResource);
                resDropdown.index = idx >= 0 ? idx : 0;
            }
            resDropdown.style.flexGrow = 1;
            resDropdown.style.fontSize = 10;
            resRow.Add(resDropdown);

            // ── Price field ──────────────────────────────────────────────────────
            var priceRow = new VisualElement();
            priceRow.style.flexDirection = FlexDirection.Row;
            priceRow.style.alignItems    = Align.Center;
            priceRow.style.marginBottom  = 5;
            form.Add(priceRow);

            var priceLbl = new Label($"{priceLabel}:");
            priceLbl.style.fontSize  = 10;
            priceLbl.style.color     = new Color(0.70f, 0.78f, 0.85f, 1f);
            priceLbl.style.width     = 65;
            priceLbl.style.flexShrink = 0;
            priceRow.Add(priceLbl);

            var priceField = new TextField();
            priceField.value      = existingPrice > 0f ? existingPrice.ToString("F1") : "";
            priceField.style.flexGrow = 1;
            priceField.style.fontSize = 10;
            priceRow.Add(priceField);

            // ── Quantity field ───────────────────────────────────────────────────
            var qtyRow = new VisualElement();
            qtyRow.style.flexDirection = FlexDirection.Row;
            qtyRow.style.alignItems    = Align.Center;
            qtyRow.style.marginBottom  = 5;
            form.Add(qtyRow);

            var qtyLbl = new Label("Max Qty:");
            qtyLbl.style.fontSize  = 10;
            qtyLbl.style.color     = new Color(0.70f, 0.78f, 0.85f, 1f);
            qtyLbl.style.width     = 65;
            qtyLbl.style.flexShrink = 0;
            qtyRow.Add(qtyLbl);

            var qtyField = new TextField();
            qtyField.value      = existingAmount > 0f ? existingAmount.ToString("F0") : "";
            qtyField.style.flexGrow = 1;
            qtyField.style.fontSize = 10;
            qtyRow.Add(qtyField);

            // ── Save / Cancel buttons ────────────────────────────────────────────
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            form.Add(btnRow);

            btnRow.Add(BuildSmallButton("Save", new Color(0.25f, 0.65f, 0.35f, 1f), () =>
            {
                string resource = resDropdown.value;
                if (string.IsNullOrEmpty(resource)) return;

                if (!float.TryParse(priceField.value, out float price) || price <= 0f) return;
                if (!float.TryParse(qtyField.value,   out float qty)   || qty   <= 0f) return;

                if (_trade == null || _station == null) return;

                if (section == "buy")  _trade.SetBuyOrder(_station,  resource, price, qty);
                else                   _trade.SetSellOrder(_station, resource, price, qty);

                _formSection      = null;
                _formEditResource = null;
                RebuildList();
            }));

            btnRow.Add(BuildSmallButton("Cancel", new Color(0.40f, 0.40f, 0.45f, 1f), () =>
            {
                _formSection      = null;
                _formEditResource = null;
                RebuildList();
            }));

            return form;
        }

        // ── Trade history section ─────────────────────────────────────────────────

        private void BuildHistorySection()
        {
            if (_trade == null || _station == null)
            {
                var none = new Label("No trade data.");
                none.style.fontSize  = 10;
                none.style.color     = new Color(0.55f, 0.60f, 0.70f, 1f);
                none.style.paddingTop = 4;
                _listRoot.Add(none);
                return;
            }

            var records = _trade.GetTradeHistory(_station, 50);
            if (records.Count == 0)
            {
                var empty = new Label("No transactions recorded yet.");
                empty.AddToClassList(EmptyClass);
                empty.style.fontSize  = 10;
                empty.style.color     = new Color(0.55f, 0.60f, 0.70f, 1f);
                empty.style.paddingTop = 4;
                _listRoot.Add(empty);
                return;
            }

            foreach (var rec in records)
                _listRoot.Add(BuildHistoryRow(rec));
        }

        private VisualElement BuildHistoryRow(TradeRecord rec)
        {
            var row = new VisualElement();
            row.AddToClassList(HistoryRowClass);
            row.style.flexDirection    = FlexDirection.Column;
            row.style.paddingTop       = 4;
            row.style.paddingBottom    = 4;
            row.style.paddingLeft      = 8;
            row.style.paddingRight     = 8;
            row.style.marginBottom     = 2;
            row.style.backgroundColor  = new Color(0.13f, 0.17f, 0.23f, 0.85f);
            row.style.borderTopLeftRadius     = 3;
            row.style.borderTopRightRadius    = 3;
            row.style.borderBottomLeftRadius  = 3;
            row.style.borderBottomRightRadius = 3;
            row.style.borderLeftWidth  = 2;
            row.style.borderLeftColor  = rec.type == "buy"
                ? new Color(0.30f, 0.75f, 0.40f, 0.8f)
                : new Color(0.90f, 0.60f, 0.20f, 0.8f);

            // Top line: tick | type | resource | qty × price
            string typeStr = rec.type == "buy" ? "BUY" : "SELL";
            var topLine = new Label(
                $"[T{rec.tick:D4}]  {typeStr}  {Capitalize(rec.resource ?? "")}  " +
                $"{rec.quantity:F0} × {rec.pricePerUnit:F1} = {rec.totalValue:F0} credits");
            topLine.style.fontSize = 10;
            topLine.style.color    = rec.type == "buy"
                ? new Color(0.60f, 0.90f, 0.65f, 1f)
                : new Color(0.95f, 0.78f, 0.45f, 1f);
            row.Add(topLine);

            // Detail line: ship name + faction (if available)
            string detail = rec.shipName ?? "Unknown ship";
            if (!string.IsNullOrEmpty(rec.faction))
                detail += $"  ·  {rec.faction}";
            var detailLine = new Label(detail);
            detailLine.style.fontSize = 9;
            detailLine.style.color    = new Color(0.55f, 0.62f, 0.72f, 1f);
            detailLine.style.paddingTop = 1;
            row.Add(detailLine);

            return row;
        }

        // ── "Add order" button ────────────────────────────────────────────────────

        private VisualElement BuildAddButton(string text, Action onClick)
        {
            var btn = new Label(text);
            btn.AddToClassList(AddBtnClass);
            btn.style.fontSize              = 10;
            btn.style.color                 = new Color(0.65f, 0.80f, 0.95f, 1f);
            btn.style.paddingTop            = 5;
            btn.style.paddingBottom         = 5;
            btn.style.paddingLeft           = 8;
            btn.style.paddingRight          = 8;
            btn.style.marginTop             = 3;
            btn.style.marginBottom          = 6;
            btn.style.backgroundColor       = new Color(0.18f, 0.26f, 0.38f, 0.9f);
            btn.style.borderTopLeftRadius     = 3;
            btn.style.borderTopRightRadius    = 3;
            btn.style.borderBottomLeftRadius  = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.borderTopWidth    = 1;
            btn.style.borderTopColor    = new Color(0.30f, 0.45f, 0.65f, 0.5f);
            btn.style.borderBottomWidth = 1;
            btn.style.borderBottomColor = new Color(0.30f, 0.45f, 0.65f, 0.5f);
            btn.style.borderLeftWidth   = 1;
            btn.style.borderLeftColor   = new Color(0.30f, 0.45f, 0.65f, 0.5f);
            btn.style.borderRightWidth  = 1;
            btn.style.borderRightColor  = new Color(0.30f, 0.45f, 0.65f, 0.5f);
            btn.style.unityTextAlign    = TextAnchor.MiddleLeft;
            btn.style.cursor            = new StyleCursor(StyleKeyword.Auto);
            btn.focusable               = true;

            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            btn.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                {
                    onClick?.Invoke();
                    evt.StopPropagation();
                }
            });
            return btn;
        }

        // ── Small inline action button (Edit / Del / Save / Cancel) ──────────────

        private VisualElement BuildSmallButton(string text, Color bgColor, Action onClick)
        {
            var btn = new Label(text);
            btn.AddToClassList(ActionBtnClass);
            btn.style.fontSize              = 9;
            btn.style.paddingTop            = 2;
            btn.style.paddingBottom         = 2;
            btn.style.paddingLeft           = 7;
            btn.style.paddingRight          = 7;
            btn.style.marginLeft            = 4;
            btn.style.backgroundColor       = bgColor;
            btn.style.color                 = new Color(1f, 1f, 1f, 1f);
            btn.style.unityTextAlign        = TextAnchor.MiddleCenter;
            btn.style.borderTopLeftRadius     = 3;
            btn.style.borderTopRightRadius    = 3;
            btn.style.borderBottomLeftRadius  = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.cursor                = new StyleCursor(StyleKeyword.Auto);
            btn.focusable                   = true;

            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            btn.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                {
                    onClick?.Invoke();
                    evt.StopPropagation();
                }
            });
            return btn;
        }

        // ── Utilities ─────────────────────────────────────────────────────────────

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
