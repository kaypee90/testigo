// Quick exit (FR-INT-5): replaces this page with a neutral one without adding a history entry.
// Without JavaScript the same control is a plain link. Nothing is stored in the browser.
(function () {
  "use strict";
  var link = document.getElementById("salir");
  if (!link) return;
  var url = link.getAttribute("data-url") || link.getAttribute("href");
  function leave(event) {
    if (event) event.preventDefault();
    try { document.body.innerHTML = ""; } catch (e) { /* keep going */ }
    window.location.replace(url);
  }
  link.addEventListener("click", leave);
  var last = 0;
  document.addEventListener("keydown", function (event) {
    if (event.key !== "Escape") return;
    var now = Date.now();
    if (now - last < 1000) leave(event);
    last = now;
  });
})();
