import { strings } from './strings.js';

const baseTranslations = {
  en: {
    overview: 'Overview',
    collection: 'Collection',
    workspace: 'Workspace',
    language: 'Language',
    theme: 'Theme',
    themeSystem: 'System',
    themeLight: 'Light',
    themeDark: 'Dark',
    open: 'Open Collection',
    create: 'Create Collection',
    createNew: 'Create New Collection',
    importOpenApi: 'Import OpenAPI',
    imported: 'Imported scenarios',
    importFailed: 'OpenAPI import failed.',
    setupTitle: 'Create Collection',
    collectionNamePlaceholder: 'Todos API Collection',
    emptyCollectionTitle: 'Start empty',
    emptyCollectionDescription:
      'Create a blank collection and add endpoints and scenarios manually.',
    importOpenApiTitle: 'Import from OpenAPI',
    importOpenApiDescription:
      'Enter a Swagger/OpenAPI URL to generate one basic scenario per endpoint.',
    openApiUrlPlaceholder: 'https://example.com/openapi.json',
    cases: 'Test Cases',
    run: 'Run Collection',
    overviewTitle: 'Collection Overview',
    overviewSub: 'Configure globals, database connections, and manage test scenarios.',
    newCase: '+ New Test Scenario',
    globals: 'Global Variables',
    connections: 'Database Connections',
    addVariable: '+ Variable',
    addConnection: '+ Connection',
    secrets: 'Secrets',
    secretsHint:
      'Reference a secret anywhere with {{secret.name}}. Only the reference is saved in the collection file, never the value.',
    addSecret: '+ Secret',
    secretName: 'name',
    secretValue: 'Value (write-only)',
    secretStored: 'Stored securely',
    secretSetValue: 'Save value',
    secretKeyPlaceholder: 'key',
    secretLocalNote: 'value stored on this machine',
    addEnvironment: '+ Environment override',
    environmentName: 'environment (e.g. ci)',
    environment: 'Environment',
    environmentDefault: 'default',
    environmentHint:
      'An environment override replaces the secret source above when that environment is selected (desktop: top bar; CLI: --env or AXIOM_ENVIRONMENT).',
    saveAll: 'Save All',
    builder: 'Visual Test Builder',
    requestStep: 'Add Request Step',
    dbStep: 'Add DB Query Step',
    saveTest: 'Save Test Case',
    delete: 'Delete',
    method: 'Method',
    endpoint: 'Endpoint',
    testName: 'Test Name',
    description: 'Description',
    step: 'Step',
    request: 'Request',
    dbQuery: 'Database query',
    stepId: 'Step ID',
    name: 'Name',
    connection: 'Connection',
    saveAs: 'Save As',
    url: 'URL',
    sql: 'SQL query',
    addAssertion: '+ Add assertion',
    assertionValue: 'source.path (e.g. body.items.*.price)',
    aggregation: 'Aggregation',
    aggregationNone: 'no aggregation',
    aggregationHint:
      'Compare a computed value instead of the value itself: count of items, or sum / avg / min / max of a list of numbers. Use * in the path to collect a field from every item, e.g. items.*.price',
    remove: 'Remove',
    yaml: 'Generated YAML',
    runSummary: 'Run Summary',
    total: 'Total',
    passed: 'Passed',
    failed: 'Failed',
    success: 'Success',
    status: 'Status',
    duration: 'Duration',
    scenario: 'Test Scenario',
    raw: 'Raw Output',
    show: 'Show output',
    hide: 'Hide output',
    notRun: 'Not run',
    noRun: 'No run data yet.',
    empty: 'No test scenarios yet.',
    chooseHint: 'Choose a collection.',
    nameRequired: 'Please enter a test scenario name.',
    endpointRequired: 'Please enter an endpoint.',
    noSteps: 'Add a request or SQL step to this scenario.',
    saved: 'Saved',
    deleted: 'Test scenario deleted',
    emptyOutput: 'No scenario rows found in output.',
    tableHint: 'Run the collection to populate this table.',
    running: 'Running collection...',
    noOutput: 'No output.',
    collectionLoaded: 'Collection loaded',
    folderSelected: 'Folder selected',
    createToContinue: 'Create a collection to continue.',
    collectionCreated: 'Collection created',
    failedCreate: 'Collection creation failed.',
    collectionName: 'Collection name',
    failedSave: 'Test scenario could not be saved.',
    deleteConfirm: 'Delete',
    noCollection: 'No collection selected',
    chooseFolder: 'Choose a folder to open or create a collection.',
    emptyFolder: 'empty folder',
    collectionSuffix: 'collection',
    caseGroup: 'Unassigned endpoint',
    noCollectionCases: 'No collection selected.',
    testResults: 'Test Results',
    resultName: 'result_name',
    showYaml: 'Show YAML',
    hideYaml: 'Hide YAML',
    queryParams: 'Query Parameters (JSON)',
    headers: 'Headers (JSON)',
    body: 'Body (JSON)',
    addQueryParameter: 'Add parameter',
  },
  tr: {
    overview: 'Genel Bakış',
    collection: 'Koleksiyon',
    workspace: 'Çalışma Alanı',
    language: 'Dil',
    theme: 'Tema',
    themeSystem: 'Sistem',
    themeLight: 'Açık',
    themeDark: 'Koyu',
    open: 'Koleksiyon Aç',
    create: 'Koleksiyon Oluştur',
    createNew: 'Yeni Koleksiyon Oluştur',
    importOpenApi: 'OpenAPI İçe Aktar',
    imported: 'İçe aktarılan senaryo sayısı',
    importFailed: 'OpenAPI içe aktarma başarısız oldu.',
    setupTitle: 'Yeni Koleksiyon Oluştur',
    collectionNamePlaceholder: 'Todos API Koleksiyonu',
    emptyCollectionTitle: 'Boş başla',
    emptyCollectionDescription:
      'Boş bir koleksiyon oluşturun ve endpoint ile senaryoları elle ekleyin.',
    importOpenApiTitle: 'OpenAPI’den içe aktar',
    importOpenApiDescription:
      'Her endpoint için bir temel senaryo oluşturmak üzere Swagger/OpenAPI URL’si girin.',
    openApiUrlPlaceholder: 'https://example.com/openapi.json',
    cases: 'Test Senaryoları',
    run: 'Koleksiyonu Çalıştır',
    overviewTitle: 'Koleksiyon Genel Bakışı',
    overviewSub: 'Global değişkenleri, veritabanı bağlantılarını ve test senaryolarını yönetin.',
    newCase: '+ Yeni Test Senaryosu',
    globals: 'Global Değişkenler',
    connections: 'Veritabanı Bağlantıları',
    addVariable: '+ Değişken',
    addConnection: '+ Bağlantı',
    secrets: 'Gizli Değerler',
    secretsHint:
      'Bir gizli değere {{secret.ad}} ile her yerden başvurun. Koleksiyon dosyasına yalnızca başvuru yazılır, değer asla yazılmaz.',
    addSecret: '+ Gizli Değer',
    secretName: 'ad',
    secretValue: 'Değer (yalnızca yazılır)',
    secretStored: 'Güvenle saklandı',
    secretSetValue: 'Değeri kaydet',
    secretKeyPlaceholder: 'anahtar',
    secretLocalNote: 'değer bu makinede saklanır',
    addEnvironment: '+ Ortama özel kaynak',
    environmentName: 'ortam (örn. ci)',
    environment: 'Ortam',
    environmentDefault: 'varsayılan',
    environmentHint:
      'Ortama özel kaynak, o ortam seçildiğinde yukarıdaki kaynağın yerine geçer (masaüstü: üst çubuk; CLI: --env veya AXIOM_ENVIRONMENT).',
    saveAll: 'Tümünü Kaydet',
    builder: 'Görsel Test Oluşturucu',
    requestStep: 'İstek Adımı Ekle',
    dbStep: 'Veritabanı Sorgusu Ekle',
    saveTest: 'Test Senaryosunu Kaydet',
    delete: 'Sil',
    method: 'Yöntem',
    endpoint: 'Endpoint',
    testName: 'Test Senaryosu Adı',
    description: 'Açıklama',
    step: 'Adım',
    request: 'İstek',
    dbQuery: 'Veritabanı sorgusu',
    stepId: 'Adım ID',
    name: 'Ad',
    connection: 'Bağlantı',
    saveAs: 'Şu adla kaydet',
    url: 'Adres',
    sql: 'SQL sorgusu',
    addAssertion: '+ Doğrulama ekle',
    assertionValue: 'kaynak.yol (örn. body.items.*.price)',
    aggregation: 'Toplulaştırma',
    aggregationNone: 'toplulaştırma yok',
    aggregationHint:
      'Değerin kendisi yerine hesaplanan bir değeri karşılaştırır: öğe sayısı veya sayı listesinin toplamı / ortalaması / en küçüğü / en büyüğü. Her öğeden bir alan toplamak için yolda * kullanın, örn. items.*.price',
    remove: 'Sil',
    yaml: 'Oluşturulan YAML',
    runSummary: 'Çalıştırma Özeti',
    total: 'Toplam',
    passed: 'Başarılı',
    failed: 'Başarısız',
    success: 'Başarı',
    status: 'Durum',
    duration: 'Süre',
    scenario: 'Test Senaryosu',
    raw: 'Ham Çıktı',
    show: 'Çıktıyı göster',
    hide: 'Çıktıyı gizle',
    notRun: 'Çalıştırılmadı',
    noRun: 'Henüz çalıştırma verisi yok.',
    empty: 'Henüz test senaryosu yok.',
    chooseHint: 'Bir koleksiyon seçin.',
    nameRequired: 'Lütfen test senaryosu adı girin.',
    endpointRequired: 'Lütfen endpoint girin.',
    noSteps: 'Bu senaryo için istek veya SQL adımı ekleyin.',
    saved: 'Kaydedildi',
    deleted: 'Test senaryosu silindi',
    emptyOutput: 'Çıktıda senaryo satırı bulunamadı.',
    tableHint: 'Tabloyu doldurmak için koleksiyonu çalıştırın.',
    running: 'Koleksiyon çalıştırılıyor...',
    noOutput: 'Çıktı yok.',
    collectionLoaded: 'Koleksiyon yüklendi',
    folderSelected: 'Klasör seçildi',
    createToContinue: 'Devam etmek için koleksiyon oluşturun.',
    collectionCreated: 'Koleksiyon oluşturuldu',
    failedCreate: 'Koleksiyon oluşturulamadı.',
    collectionName: 'Koleksiyon adı',
    failedSave: 'Test senaryosu kaydedilemedi.',
    deleteConfirm: 'Sil',
    noCollection: 'Koleksiyon seçilmedi',
    chooseFolder: 'Bir klasör seçerek koleksiyon açın veya oluşturun.',
    emptyFolder: 'boş klasör',
    collectionSuffix: 'koleksiyonu',
    caseGroup: 'Atanmamış endpoint',
    noCollectionCases: 'Koleksiyon seçilmedi.',
    testResults: 'Test Sonuçları',
    resultName: 'sonuc_adi',
    showYaml: 'YAML göster',
    hideYaml: 'YAML gizle',
    queryParams: 'Query Parametreleri (JSON)',
    headers: 'Header’lar (JSON)',
    body: 'Body (JSON)',
    addQueryParameter: 'Parametre ekle',
  },
};

export const translations = {
  en: { ...baseTranslations.en, ...strings.en },
  tr: { ...baseTranslations.tr, ...strings.tr },
};

export function getLanguage() {
  return localStorage.getItem('axiom-language') || 'en';
}
export function getTheme() {
  return localStorage.getItem('axiom-theme') || 'system';
}
export function methodSupportsBody(method) {
  return ['POST', 'PUT', 'PATCH', 'DELETE'].includes(String(method).toUpperCase());
}
export function groupByEndpoint(items, t) {
  const groups = new Map();
  items.forEach((item) => {
    const label = `${item.method || 'REQUEST'} ${item.endpoint || t.caseGroup}`;
    if (!groups.has(label)) groups.set(label, []);
    groups.get(label).push(item);
  });
  return [...groups.entries()].map(([label, groupItems]) => ({ label, items: groupItems }));
}
export function normalizeSteps(items) {
  return items.map((step, index) => ({
    id: step.id || `step_${index + 1}`,
    type: step.type || 'request',
    name: step.name || (step.type === 'include' ? '' : `Step ${index + 1}`),
    method: step.method || 'GET',
    url: step.url || '{{base_url}}',
    queryParams: toKeyValueRows(step.queryParams || step.query_params),
    headers: toKeyValueRows(step.headers),
    body: step.body || '',
    connection: step.connection || '',
    sql: step.sql || '',
    save_as: step.save_as || step.saveAs || '',
    ref: step.ref || '',
    assertions: (step.assert || []).map((a) => ({
      expression: [a.source, a.path].filter(Boolean).join('.'),
      aggregate: a.aggregate || '',
      strict: Boolean(a.strict),
      caseSensitive: Boolean(a.caseSensitive ?? a.case_sensitive),
      tolerance: a.tolerance == null ? '' : String(a.tolerance),
      operator: a.operator || '==',
      expected: String(a.expected ?? ''),
    })),
  }));
}
// The builder edits an assertion's source and path as one dotted expression (e.g. body.items.*.price).
// The YAML keeps them separate: the first segment is the source, the rest is the path.
function splitExpression(expression) {
  const text = (expression || '').trim();
  const dot = text.indexOf('.');
  if (dot < 0) return { source: text, path: undefined };
  return { source: text.slice(0, dot), path: text.slice(dot + 1) || undefined };
}

// Variable and result names may contain letters and underscores only.
export function toVariableName(value) {
  return value.replace(/[^A-Za-z_]/g, '');
}

export function toYamlSteps(steps) {
  return steps.map((step) => ({
    id: step.id,
    type: step.type,
    name: step.name,
    method: step.type === 'request' ? step.method : undefined,
    url: step.type === 'request' ? step.url : undefined,
    queryParams: step.type === 'request' ? rowsToObject(step.queryParams) : undefined,
    headers: step.type === 'request' ? rowsToObject(step.headers) : undefined,
    body:
      step.type === 'request' && methodSupportsBody(step.method)
        ? step.body || undefined
        : undefined,
    connection: step.type === 'db_query' ? step.connection : undefined,
    sql: step.type === 'db_query' ? step.sql : undefined,
    saveAs: step.save_as || undefined,
    ref: step.type === 'include' ? step.ref : undefined,
    assert: step.assertions.map((a) => ({
      ...splitExpression(a.expression),
      aggregate: a.aggregate || undefined,
      strict: a.strict || undefined,
      caseSensitive: a.caseSensitive || undefined,
      tolerance:
        a.operator === 'approx' && a.tolerance !== '' && !Number.isNaN(Number(a.tolerance))
          ? Number(a.tolerance)
          : undefined,
      operator: a.operator,
      expected: String(a.expected ?? ''),
    })),
  }));
}

function toKeyValueRows(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
  return Object.entries(value).map(([key, item]) => ({ key, value: String(item ?? '') }));
}

function rowsToObject(rows) {
  return Object.fromEntries(
    (rows || []).filter((row) => row.key.trim()).map((row) => [row.key.trim(), row.value]),
  );
}
export function yamlPreview(test, t) {
  if (!test.steps.length) return t.noSteps;
  return [
    `name: '${test.name}'`,
    `description: '${test.description}'`,
    ...(test.run !== undefined
      ? [`run: ${test.run || 'each'}`]
      : [`method: '${test.method}'`, `endpoint: '${test.endpoint}'`]),
    'steps:',
    ...toYamlSteps(test.steps).flatMap((step) => [
      `  - id: '${step.id}'`,
      `    type: '${step.type}'`,
      `    name: '${step.name}'`,
      ...(step.type === 'include'
        ? [`    ref: '${step.ref}'`]
        : step.type === 'request'
          ? [
              `    method: '${step.method}'`,
              `    url: '${step.url}'`,
              `    query_params: ${JSON.stringify(step.queryParams || {})}`,
              `    headers: ${JSON.stringify(step.headers || {})}`,
              ...(step.body ? [`    body: '${step.body.replaceAll("'", "''")}'`] : []),
            ]
          : [`    connection: '${step.connection}'`, `    sql: '${step.sql}'`]),
    ]),
  ].join('\n');
}
// Turns the host's structured run result into what the results screen renders.
export function buildReport(result, tests) {
  const byFile = new Map(tests.map((test) => [test.fileName, test]));
  const cases = (result.testCases || []).map((testCase) => {
    const fileName = String(testCase.sourceFile || '')
      .split(/[\\/]/)
      .pop();
    const known = byFile.get(fileName) || {};
    return {
      name: testCase.name,
      fileName,
      method: known.method,
      endpoint: known.endpoint,
      passed: Boolean(testCase.passed),
      durationMs: new Date(testCase.completedAt) - new Date(testCase.startedAt),
      steps: (testCase.steps || []).map(mapStep),
    };
  });
  const passed = cases.filter((item) => item.passed).length;
  return {
    total: cases.length,
    passed,
    failed: cases.length - passed,
    rate: cases.length ? ((passed / cases.length) * 100).toFixed(0) : '0',
    durationMs: new Date(result.completedAt) - new Date(result.startedAt),
    completedAt: new Date(result.completedAt).toLocaleTimeString(),
    tests: cases,
  };
}

function mapStep(step) {
  return {
    name: step.name,
    type: step.type,
    passed: Boolean(step.passed),
    error: step.error,
    statusCode: step.statusCode,
    rowCount: step.rowCount,
    durationMs: step.durationMs,
    children: (step.children || []).map(mapStep),
    assertions: (step.assertions || []).map((assertion) => ({
      text: describeAssertion(assertion),
      passed: Boolean(assertion.passed),
      expected: assertion.expected,
      actual: assertion.actual,
      error: assertion.error,
    })),
  };
}

function describeAssertion(assertion) {
  const target = `${assertion.source}${assertion.path ? `.${assertion.path}` : ''}`;
  const aggregate = assertion.aggregate ? `${assertion.aggregate}(${target})` : target;
  const needsValue = !['exists', 'not_exists'].includes(assertion.operator);
  return `${aggregate} ${assertion.operator}${needsValue ? ` ${String(assertion.expected ?? '')}` : ''}`;
}
