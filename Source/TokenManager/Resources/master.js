﻿/// <reference path="angular.js" />
(function () {
	'use strict';

	angular
        .module('app')
        .controller('master', master);


	function master() {
		var vm = this;
		vm.isRTE = typeof (window.parent.scTokenSelectorCallback) !== "undefined";
		vm.activePage = "selector";
	    vm.events = {
	        'click': function(val) {
	            vm.events.selected = val;
	        }
	    };
		activate();

		function activate() { }
	}
})();
$('.fancybox').fancybox({
	width: '90%', height: '90%', fitToView: false, autoSize: false
});