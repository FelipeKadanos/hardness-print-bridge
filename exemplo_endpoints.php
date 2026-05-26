<?php
    /*
     * Exemplo de endpoints do sistema Hardness para integração com o
     * Hardness Print Bridge (HPB).
     *
     * Este arquivo funciona como referência de contrato HTTP:
     * - quais rotas existem;
     * - quais parâmetros são recebidos;
     * - qual formato de resposta é retornado.
     *
     * Objetivo:
     * acelerar desenvolvimento e troubleshooting mantendo o comportamento
     * esperado entre o HPB e o sistema Hardness.
     *
     * Observações:
     * - é um arquivo de exemplo/documentação técnica;
     * - pode ser ajustado quando o contrato oficial do Hardness evoluir.
     */

    /* Endpoints:
     * http://localhost/api/rel/list_files?API_AUTH=5f191cea3067142ad1c104103178da49
     * http://localhost/api/rel/select_file?API_AUTH=5f191cea3067142ad1c104103178da49&arquivo={fileName}
     * http://localhost/api/rel/callback?API_AUTH=5f191cea3067142ad1c104103178da49
     */

    case 'list_files':
		$retorno = $API003->auth($_GET['API_AUTH'], false);

		if(is_array($retorno)){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode($retorno, JSON_UNESCAPED_UNICODE);
			die();
		}

		$diretorioBase = obterDiretorioTmpImpressao();
		$limite = isset($_GET['limite']) ? (int)$_GET['limite'] : 200;
		if($limite <= 0){
			$limite = 200;
		}
		if($limite > 1000){
			$limite = 1000;
		}

		if(!file_exists($diretorioBase) || !is_dir($diretorioBase)){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode(array(
				'sucesso' => false,
				'erro' => 'Diretorio tmp/impressao nao encontrado no pathDados.',
				'diretorio' => $diretorioBase
			), JSON_UNESCAPED_UNICODE);
			die();
		}

		$itens = scandir($diretorioBase);
		$itensValidos = array();

		foreach($itens as $item){
			if($item === '.' || $item === '..'){
				continue;
			}
			$itensValidos[] = $item;
		}

		usort($itensValidos, function($a, $b) use ($diretorioBase){
			$arquivoA = $diretorioBase . '/' . $a;
			$arquivoB = $diretorioBase . '/' . $b;
			$mtimeA = @filemtime($arquivoA);
			$mtimeB = @filemtime($arquivoB);

			if($mtimeA == $mtimeB){
				return 0;
			}
			return ($mtimeA > $mtimeB) ? -1 : 1;
		});

		$arquivos = array();
		$totalArquivos = 0;
		$totalDiretorios = 0;
		$totalBytes = 0;

		foreach($itensValidos as $item){
			$caminhoItem = $diretorioBase . '/' . $item;
			$eDiretorio = is_dir($caminhoItem);

			if($eDiretorio){
				$totalDiretorios++;
			} else {
				$totalArquivos++;
				$totalBytes += (int)filesize($caminhoItem);
			}

			if(count($arquivos) >= $limite){
				continue;
			}

			$arquivos[] = array(
				'nome' => $item,
				'tipo' => $eDiretorio ? 'diretorio' : 'arquivo',
				'tamanho_bytes' => $eDiretorio ? 0 : (int)filesize($caminhoItem),
				'modificado_em' => date('Y-m-d H:i:s', filemtime($caminhoItem))
			);
		}

		$resposta = array(
			'sucesso' => true,
			'diretorio' => $diretorioBase,
			'total_itens_retornados' => count($arquivos),
			'limite' => $limite,
			'arquivos' => $arquivos
		);

		header('Content-Type: application/json; charset=utf-8');
		echo json_encode($resposta, JSON_UNESCAPED_UNICODE);
		die();
	break;

	case 'select_file':
		$retorno = $API003->auth($_GET['API_AUTH'], false);

		if(is_array($retorno)){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode($retorno, JSON_UNESCAPED_UNICODE);
			die();
		}

		$arquivo = isset($_GET['arquivo']) ? trim($_GET['arquivo']) : (isset($_GET['file']) ? trim($_GET['file']) : '');
		$arquivo = basename($arquivo);
		if($arquivo === ''){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode(array(
				'sucesso' => false,
				'erro' => 'Parametro arquivo e obrigatorio.'
			), JSON_UNESCAPED_UNICODE);
			die();
		}

		$diretorioBase = obterDiretorioTmpImpressao();
		$caminhoArquivo = $diretorioBase . '/' . $arquivo;
		if(!is_file($caminhoArquivo)){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode(array(
				'sucesso' => false,
				'erro' => 'Arquivo nao encontrado.',
				'arquivo' => $arquivo
			), JSON_UNESCAPED_UNICODE);
			die();
		}

		$conteudo = file_get_contents($caminhoArquivo);
		if($conteudo === false){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode(array(
				'sucesso' => false,
				'erro' => 'Nao foi possivel ler o arquivo.',
				'arquivo' => $arquivo
			), JSON_UNESCAPED_UNICODE);
			die();
		}

		header('Content-Type: text/plain; charset=utf-8');
		header('Content-Disposition: inline; filename="' . $arquivo . '"');
		header('Content-Length: ' . strlen($conteudo));
		echo $conteudo;
		die();
	break;

	case 'callback':
		$retorno = $API003->auth($_GET['API_AUTH'], false);

		if(is_array($retorno)){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode($retorno, JSON_UNESCAPED_UNICODE);
			die();
		}

		$payload = json_decode(file_get_contents('php://input'), true);
		if(!is_array($payload)){
			$payload = array();
		}

		$arquivo = isset($_POST['arquivo']) ? trim($_POST['arquivo']) : (isset($payload['arquivo']) ? trim($payload['arquivo']) : '');
		$acao = isset($_POST['acao']) ? trim($_POST['acao']) : (isset($payload['acao']) ? trim($payload['acao']) : '');
		$status = isset($_POST['status']) ? trim($_POST['status']) : (isset($payload['status']) ? trim($payload['status']) : '');
		$texto = isset($_POST['texto']) ? trim($_POST['texto']) : (isset($payload['texto']) ? trim($payload['texto']) : '');

		$camposFaltando = array();
		if($arquivo === ''){
			$camposFaltando[] = 'arquivo';
		}
		if($acao === ''){
			$camposFaltando[] = 'acao';
		}
		if($status === ''){
			$camposFaltando[] = 'status';
		}
		if($texto === ''){
			$camposFaltando[] = 'texto';
		}

		if(!empty($camposFaltando)){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode(array(
				'sucesso' => false,
				'erro' => 'Campos obrigatorios ausentes: ' . implode(', ', $camposFaltando) . '.'
			), JSON_UNESCAPED_UNICODE);
			die();
		}

		$retornoInsert = inserirCallbackP005($arquivo, $acao, $status, $texto);
		if($retornoInsert !== true){
			header('Content-Type: application/json; charset=utf-8');
			echo json_encode(array(
				'sucesso' => false,
				'erro' => $retornoInsert
			), JSON_UNESCAPED_UNICODE);
			die();
		}

		header('Content-Type: application/json; charset=utf-8');
		echo json_encode(array(
			'sucesso' => true,
			'dados' => array(
				'arquivo' => $arquivo,
				'acao' => $acao,
				'status' => $status,
				'texto' => $texto
			)
		), JSON_UNESCAPED_UNICODE);
		die();
	break;


// Funções auxiliares

function obterDiretorioTmpImpressao(){
	global $g;

	$diretorioBase = rtrim($g['pathDados'], '/') . '/tmp/impressao';
	if((!file_exists($diretorioBase) || !is_dir($diretorioBase)) && is_dir('/tmp/impressao')){
		$diretorioBase = '/tmp/impressao';
	}

	return $diretorioBase;
}

function deletarArquivoTmpImpressao($arquivo){
	$arquivoOriginal = trim((string)$arquivo);
	$arquivo = basename($arquivoOriginal);
	if($arquivo === ''){
		return 'Parametro "arquivo" nao informado ou vazio.';
	}

	if($arquivo !== $arquivoOriginal){
		return 'Nome de arquivo invalido. Informe somente o nome do arquivo, sem caminho.';
	}

	$diretorioBase = obterDiretorioTmpImpressao();
	$caminhoArquivo = $diretorioBase . '/' . $arquivo;
	if(!is_file($caminhoArquivo)){
		return 'Arquivo nao encontrado para exclusao em: ' . $caminhoArquivo;
	}

	$erroUnlink = '';
	set_error_handler(function($errno, $errstr) use (&$erroUnlink){
		$erroUnlink = $errstr;
		return true;
	});
	$deletou = unlink($caminhoArquivo);
	restore_error_handler();

	if(!$deletou){
		if($erroUnlink !== ''){
			return 'Falha ao deletar arquivo "' . $caminhoArquivo . '": ' . $erroUnlink;
		}
		return 'Falha ao deletar arquivo "' . $caminhoArquivo . '" sem detalhe retornado pelo sistema.';
	}

	return true;
}

function inserirCallbackP005($arquivo, $acao, $status, $texto){
	$arquivo = mysql_real_escape_string($arquivo);
	$acao = mysql_real_escape_string($acao);
	$status = mysql_real_escape_string($status);
	$texto = mysql_real_escape_string($texto);

	$sql = "INSERT INTO P005 (
				P005_Arquivo,
				P005_Acao,
				P005_Status,
				P005_Texto,
				P005_Data_Hora
			) VALUES (
				'{$arquivo}',
				'{$acao}',
				'{$status}',
				'{$texto}',
				NOW()
			)";

	$res = mysql_query($sql);
	if(!$res){
		return 'Erro ao inserir callback na P005: ' . mysql_error();
	}

	return true;
}
