const nodeFlagChoices = [
  ["", "无国旗"],
  ["CN", "🇨🇳 中国"],
  ["JP", "🇯🇵 日本"],
  ["US", "🇺🇸 美国"],
  ["SG", "🇸🇬 新加坡"],
  ["HK", "🇭🇰 中国香港"],
  ["KR", "🇰🇷 韩国"],
  ["DE", "🇩🇪 德国"],
  ["GB", "🇬🇧 英国"],
  ["FR", "🇫🇷 法国"]
];

function normalizedNodeName(value) {
  const raw = String(value || "").trim();
  for (const flag of ["🇨🇳", "🇯🇵", "🇺🇸", "🇸🇬", "🇭🇰", "🇰🇷", "🇩🇪", "🇬🇧", "🇫🇷"]) {
    if (raw.replace(/️/g, "").startsWith(flag)) return raw.slice(flag.length).trimStart();
  }
  return raw;
}

function inferredNodeFlag(value) {
  const raw = String(value || "").replace(/️/g, "").trimStart();
  return raw.startsWith("🇨🇳") ? "CN" : raw.startsWith("🇯🇵") ? "JP" : raw.startsWith("🇺🇸") ? "US"
    : raw.startsWith("🇸🇬") ? "SG" : raw.startsWith("🇭🇰") ? "HK" : raw.startsWith("🇰🇷") ? "KR"
      : raw.startsWith("🇩🇪") ? "DE" : raw.startsWith("🇬🇧") ? "GB" : raw.startsWith("🇫🇷") ? "FR" : "";
}

function nodeFlagSelect(id, flagCode) {
  return '<select class="node-flag-select" data-id="' + escapeHtml(id) + '">'
    + nodeFlagChoices.map(([code, label]) => '<option value="' + code + '"' + (code === flagCode ? " selected" : "") + '>' + label + "</option>").join("")
    + "</select>";
}

function nodeRow(node, isLocal) {
  const id = isLocal ? "local" : node.id;
  const name = normalizedNodeName(node.name || (isLocal ? "本机节点" : id));
  const flagCode = node.flagCode || inferredNodeFlag(node.name);
  const host = node.publicHost || "";
  const version = isLocal ? "local" : (node.version || "等待接入");
  const status = node.online ? "在线" : "离线";
  const actions = '<button class="node-save" data-id="' + escapeHtml(id) + '">保存</button>'
    + (isLocal ? " <span class=\"node-local\">本机</span>"
      : ' <button class="node-restart" data-id="' + escapeHtml(id) + '">重启</button> <button class="danger node-delete" data-id="' + escapeHtml(id) + '">删除</button>');
  return '<tr><td><div class="node-editor">' + nodeFlagSelect(id, flagCode)
    + '<input class="node-name-edit" data-id="' + escapeHtml(id) + '" value="' + escapeHtml(name) + '"></div><small>' + escapeHtml(version) + "</small></td>"
    + '<td><input class="node-host-edit" data-id="' + escapeHtml(id) + '" value="' + escapeHtml(host) + '"><small>frps : ' + (node.frpsPort || 7000) + "</small></td>"
    + '<td><span class="tag ' + (node.online ? "" : "off") + '">' + status + "</span></td>"
    + "<td>" + (node.activeClients || 0) + "</td><td>" + (node.activeProxies || 0) + "</td>"
    + "<td>" + (isLocal ? "刚刚" : new Date(node.lastSeen).toLocaleString()) + "</td><td>" + actions + "</td></tr>";
}

loadNodes = async function () {
  try {
    const [status, nodes] = await Promise.all([api("/api/frps/install-status"), api("/api/admin/nodes")]);
    const overview = snapshot || await api("/api/overview");
    $("#install-status").textContent = status.message || (status.installed ? "本机 frps 已安装，可由主控面板管理。" : "本机尚未安装，可点击右上角自动修复。");
    const local = {
      name: overview.localNodeName || "本机节点",
      flagCode: overview.localNodeFlagCode || "",
      publicHost: overview.publicHost || "",
      frpsPort: overview.bindPort || 7000,
      online: overview.reachable,
      activeClients: Number($("#metric-clients").textContent || 0),
      activeProxies: Number($("#metric-proxies").textContent || 0)
    };
    $("#nodes-body").innerHTML = nodeRow(local, true) + nodes.map(node => nodeRow(node, false)).join("");
    $$(".node-save").forEach(button => button.onclick = async () => {
      try {
        const id = button.dataset.id;
        const name = $('.node-name-edit[data-id="' + id + '"]').value;
        const flagCode = $('.node-flag-select[data-id="' + id + '"]').value;
        const publicHost = $('.node-host-edit[data-id="' + id + '"]').value;
        await api("/api/admin/nodes/" + id, { method: "PUT", body: JSON.stringify({ name, flagCode, publicHost }) });
        toast("节点信息已保存");
        snapshot = null;
        loadNodes();
      } catch (error) { toast(error.message); }
    });
    $$(".node-restart").forEach(button => button.onclick = async () => {
      try {
        const result = await api("/api/admin/nodes/" + button.dataset.id + "/service/restart", { method: "POST" });
        toast(result.message);
      } catch (error) { toast(error.message); }
    });
    $$(".node-delete").forEach(button => button.onclick = async () => {
      try {
        const result = await api("/api/admin/nodes/" + button.dataset.id, { method: "DELETE" });
        toast(result.message);
        snapshot = null;
        loadNodes();
      } catch (error) { toast(error.message); }
    });
  } catch (error) { toast(error.message); }
};

$("#node-enrollment-form").onsubmit = async event => {
  event.preventDefault();
  const status = $("#node-enrollment-status");
  const command = $("#node-enrollment-command");
  status.textContent = "正在生成节点部署命令...";
  try {
    const result = await api("/api/admin/nodes/enrollment", {
      method: "POST",
      body: JSON.stringify({
        name: $("#node-enrollment-name").value,
        publicHost: $("#node-enrollment-host").value,
        masterUrl: $("#node-enrollment-master-url").value,
        flagCode: $("#node-enrollment-flag").value
      })
    });
    command.value = result.command;
    command.classList.remove("hidden");
    $("#node-enrollment-actions").classList.remove("hidden");
    status.textContent = "节点 " + result.name + " 已登记，执行下方命令后将自动上线。";
    toast("节点部署命令已生成");
    snapshot = null;
    loadNodes();
  } catch (error) {
    status.textContent = error.message;
    toast(error.message);
  }
};
