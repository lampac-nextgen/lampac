module.exports.onStart = function() { 
	console.log("Tizen", "service", "start callback"); 
}

module.exports.onRequest = function() { 
	console.log("Tizen", "service", "request callback"); 

	var reqAppControl = tizen.application.getCurrentApplication().getRequestedAppControl();

	if (reqAppControl && reqAppControl.appControl.operation == "http://tizen.org/appcontrol/operation/pick") { 
		var data = reqAppControl.appControl.data;
		
		if (data[0].value[0] == 'ForegroundApp') {
			var json = data[0].value[1]

			webapis.preview.setPreviewData(json,
				function() {
					//tizen.application.getCurrentApplication().exit(); 
				}, 
				function(e) { 
					console.log("Tizen", "service", "setPreviewData failed : " + e.message); 
				} 
			); 
		} 
	} 
}

module.exports.onExit = function() { 
	console.log("Tizen", "service", "exit callback"); 
} 