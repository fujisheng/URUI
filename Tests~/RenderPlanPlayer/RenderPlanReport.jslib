mergeInto(LibraryManager.library, {
  UruiReportRenderPlanContract: function (value) {
    var json = UTF8ToString(value);
    fetch('/render-plan-result', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: json });
    document.body.setAttribute('data-urui-contract-result', json);
  }
});
