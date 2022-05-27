package main

import "github.com/yknext/goudpx/pkg/service"

func main() {

	srv := service.NewService()
	srv.Run(":7777")

}
