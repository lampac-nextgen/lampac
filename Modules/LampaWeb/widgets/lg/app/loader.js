console.log('Start load');

var lampac_url = localStorage.getItem('lampac_url');
var cache_version = Math.floor((new Date()).getTime() / 9e5);

var isLampac = localStorage.getItem('lampac_is_server');

if (!localStorage.getItem('lampac_lg_loader_fix_v1')) {
    localStorage.removeItem('lampac_initiale');
    localStorage.setItem('lampac_lg_loader_fix_v1', 'true');
}

if (!lampac_url) {
    lampac_url = '{localhost}';
    localStorage.setItem('lampac_url', lampac_url);
}

if (isLampac === null) {
    isLampac = 'true';
    localStorage.setItem('lampac_is_server', isLampac);
}

function urlJoin(base, add) {
    if (base.charAt(base.length - 1) !== '/') {
        base += '/'
    }

    return base + add
}

function createScript(src,error){
    console.log('Load script:' + src);

    var script         = document.createElement('script');
        script.onerror = error;
        script.src     = src;
        script.type    = 'text/javascript';

    document.getElementsByTagName("body")[0].appendChild(script);
}

function startAppWithDeepLink(){
    createScript(urlJoin(lampac_url, 'app.min.js?v' + cache_version), function(){
        console.log('app.min.js fail');

        loadFromLocal()
    })

    if (isLampac === 'true') {
        console.log('this is Lampac, we will use lampainit.js');
        createScript(urlJoin(lampac_url, 'lampainit.js?v=' + cache_version), function(){
            console.log('lampainit fail');
        })
    }
}

function saveToLocal(){
    var request = new XMLHttpRequest();

    request.onload = function() {
        if (this.readyState == 4 && this.status == 200) {
            window.localStorage.setItem('app.js',this.responseText)

            console.log('Saved in storage')
        }
    };

    request.onerror = function () {

    };

    request.open('GET', urlJoin(lampac_url, 'app.min.js?v' + cache_version));
    request.send();
}

function loadFromLocal(){
	if(window.appready) return
	
    var app = window.localStorage.getItem('app.js')

    if(app){
        console.log('Try eval app')
        
        try{
            eval(app)
        }
        catch(e){
            createScript('app.js', function(){
                console.log('Load local error');
            })
        }
    }
    else{
        createScript('app.js', function(){
            console.log('Load local error');
        })
    }
}

function checkConnection(url, successCb, errorCb) {
    var xhr = new XMLHttpRequest();
    var executed = false;

    xhr.open('GET', url, true);
    xhr.onload = function () {
        if (executed) {
            return;
        }
        executed = true;
        if (xhr.status == '200') {
            successCb && successCb(xhr);
        } else {
            errorCb && errorCb(xhr);
        }
    };
    xhr.onerror = function () {
        if (executed) {
            return;
        }
        executed = true;
        errorCb && errorCb(xhr);
    };
    xhr.ontimeout = function () {
        if (executed) {
            return;
        }
        executed = true;
        errorCb && errorCb(xhr);
    };
    xhr.send(null); 
}


function countdown() {
    if (timeLeft == 0) {
        clearTimeout(timerId);

        startAppWithDeepLink();

        saveToLocal();
    }
    else {
        checkConnection(
            urlJoin(lampac_url, 'app.min.js?v' + cache_version),
            function () {
                if(!app_loaded){
                    app_loaded = true

                    clearTimeout(timerId);

                    startAppWithDeepLink();

                    saveToLocal();
                }
            },
            function () {
                console.log('No Network');
            });
    }

    timeLeft--;
}

var timeLeft   = 5;
var timerId    = -1;
var app_loaded = false;

if (lampac_url) {
	timerId = setInterval(countdown, 3000);
	}
	else {
		lampac_url = '{localhost}';
		localStorage.setItem('lampac_url', lampac_url);
		timerId = setInterval(countdown, 3000);
	}

function initLampaApp(isLampacServer) {
	lampac_url = localStorage.getItem('lampac_url');
    isLampac = isLampacServer ? 'true' : '';
    localStorage.setItem('lampac_is_server', isLampac);
    timerId = setInterval(countdown, 3000);
}


